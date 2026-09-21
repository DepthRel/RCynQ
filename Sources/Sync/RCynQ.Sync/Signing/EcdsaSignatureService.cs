using System.Security.Cryptography;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

namespace RCynQ.Sync.Signing;

/// <summary>
/// <see cref="ISignatureService"/> using ECDSA P-256 with SHA-256 (IEEE P1363 signatures).
/// </summary>
/// <remarks>
/// The node key pair is created on first use and stored as PKCS#8 in the encrypted secret store under
/// <see cref="SecretName"/>; it never leaves the node.
/// </remarks>
public sealed class EcdsaSignatureService : ISignatureService, IDisposable
{
    /// <summary>
    /// The secret store entry holding the private key.
    /// </summary>
    public const string SecretName = "node-signing-key";

    /// <summary>
    /// The secret store.
    /// </summary>
    private readonly ISecretStore _secrets;

    /// <summary>
    /// Guards key loading.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// The loaded key.
    /// </summary>
    private ECDsa? _key;

    /// <summary>
    /// Initializes a new instance of the <see cref="EcdsaSignatureService"/> class.
    /// </summary>
    /// <param name="secrets">The secret store.</param>
    public EcdsaSignatureService(ISecretStore secrets)
    {
        _secrets = secrets;
    }

    /// <inheritdoc />
    public async Task<byte[]> SignAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var key = await GetKeyAsync(cancellationToken);
        return key.SignData(payload, HashAlgorithmName.SHA256);
    }

    /// <inheritdoc />
    public bool Verify(byte[] payload, byte[] signature, byte[] publicKeyDer)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(publicKeyDer);
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKeyDer, out var bytesRead);
            return bytesRead == publicKeyDer.Length
                && key.KeySize == 256
                && key.VerifyData(payload, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportPublicKeyAsync(CancellationToken cancellationToken = default)
        => (await GetKeyAsync(cancellationToken)).ExportSubjectPublicKeyInfo();

    /// <summary>
    /// Releases the loaded key.
    /// </summary>
    public void Dispose()
    {
        _key?.Dispose();
        _lock.Dispose();
    }

    /// <summary>
    /// Loads or creates the key pair.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The key.</returns>
    /// <exception cref="SyncBridgeException">The stored key is malformed.</exception>
    private async Task<ECDsa> GetKeyAsync(CancellationToken cancellationToken)
    {
        if (_key is not null)
        {
            return _key;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_key is not null)
            {
                return _key;
            }

            var stored = await _secrets.GetAsync(SecretName, cancellationToken);
            var key = ECDsa.Create();
            if (stored is null)
            {
                key.GenerateKey(ECCurve.NamedCurves.nistP256);
                await _secrets.SetAsync(SecretName, Convert.ToBase64String(key.ExportPkcs8PrivateKey()), cancellationToken);
            }
            else
            {
                try
                {
                    key.ImportPkcs8PrivateKey(Convert.FromBase64String(stored), out _);
                }
                catch (Exception ex) when (ex is FormatException or CryptographicException)
                {
                    key.Dispose();
                    throw new SyncBridgeException(Texts.Get("Signing.KeyMalformed"), ex);
                }
            }

            _key = key;
            return key;
        }
        finally
        {
            _lock.Release();
        }
    }
}
