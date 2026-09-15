using RCynQ.Core.Exceptions;

namespace RCynQ.Core.Abstractions.Security;

/// <summary>
/// Signs and verifies patch payloads.
/// </summary>
/// <remarks>
/// The canonical implementation uses ECDSA P-256 with SHA-256. Each node signs with its own private key,
/// which never leaves the node; peers verify with the public key exchanged via export/import.
/// </remarks>
public interface ISignatureService
{
    /// <summary>
    /// Signs a payload with the local private key, creating the key pair on first use.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The signature bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="SyncBridgeException">The local key could not be loaded.</exception>
    Task<byte[]> SignAsync(byte[] payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a signature with the given public key.
    /// </summary>
    /// <param name="payload">The signed payload.</param>
    /// <param name="signature">The signature.</param>
    /// <param name="publicKeyDer">The DER SubjectPublicKeyInfo public key of the signer.</param>
    /// <returns><see langword="true"/> when the signature is valid; <see langword="false"/> for an invalid signature or malformed key.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    bool Verify(byte[] payload, byte[] signature, byte[] publicKeyDer);

    /// <summary>
    /// Exports the local public key, creating the key pair on first use.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The DER SubjectPublicKeyInfo bytes.</returns>
    /// <exception cref="SyncBridgeException">The local key could not be loaded.</exception>
    Task<byte[]> ExportPublicKeyAsync(CancellationToken cancellationToken = default);
}
