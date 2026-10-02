using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Data.Context;
using RCynQ.Core.Data.Entities;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Validation;
using RCynQ.Localization;

namespace RCynQ.Core.Data.Storage;

/// <summary>
/// <see cref="ISecretStore"/> that encrypts values with AES-256-GCM and stores the ciphertext in SQLite.
/// </summary>
/// <remarks>
/// The 256-bit data key lives in <c>keys/secrets.key</c>, created on first use with owner-only permissions on
/// Unix-like systems. The secret name is bound as associated data, so a ciphertext cannot be moved to a
/// different name. Tampered values fail authentication and raise <see cref="SyncBridgeException"/>.
/// </remarks>
public sealed class AesGcmSecretStore : ISecretStore
{
    /// <summary>
    /// The key file name inside the keys directory.
    /// </summary>
    public const string KEY_FILE_NAME = "secrets.key";

    /// <summary>
    /// AES key size in bytes.
    /// </summary>
    private const int KEY_SIZE = 32;

    /// <summary>
    /// GCM nonce size in bytes.
    /// </summary>
    private const int NONCE_SIZE = 12;

    /// <summary>
    /// GCM tag size in bytes.
    /// </summary>
    private const int TAG_SIZE = 16;

    /// <summary>
    /// The context factory.
    /// </summary>
    private readonly IDbContextFactory<RCynQDbContext> _contextFactory;

    /// <summary>
    /// The application data factory.
    /// </summary>
    private readonly IAppDataFactory _appDataFactory;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Guards key file creation.
    /// </summary>
    private readonly SemaphoreSlim _keyLock = new(1, 1);

    /// <summary>
    /// The cached data key.
    /// </summary>
    private byte[]? _key;

    /// <summary>
    /// The localizer of the messages.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AesGcmSecretStore"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="contextFactory">The context factory.</param>
    /// <param name="appDataFactory">The application data factory.</param>
    /// <param name="timeProvider">The clock.</param>
    public AesGcmSecretStore(ILocalizer localizer, IDbContextFactory<RCynQDbContext> contextFactory, IAppDataFactory appDataFactory, TimeProvider timeProvider)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _contextFactory = contextFactory;
        _appDataFactory = appDataFactory;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task SetAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        var secretName = Guard.NotBlank(name, nameof(name));
        ArgumentNullException.ThrowIfNull(value);
        var key = await GetKeyAsync(cancellationToken);

        var nonce = RandomNumberGenerator.GetBytes(NONCE_SIZE);
        var plain = Encoding.UTF8.GetBytes(value);
        var cipher = new byte[plain.Length];
        var tag = new byte[TAG_SIZE];
        using (var aes = new AesGcm(key, TAG_SIZE))
        {
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(secretName));
        }

        CryptographicOperations.ZeroMemory(plain);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Secrets.FindAsync(new object[] { secretName }, cancellationToken);
        if (entity is null)
        {
            entity = new SecretEntity { Name = secretName };
            db.Secrets.Add(entity);
        }

        entity.Nonce = nonce;
        entity.CipherText = cipher;
        entity.Tag = tag;
        entity.UpdatedAtUtc = _timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var secretName = Guard.NotBlank(name, nameof(name));
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Secrets.AsNoTracking().SingleOrDefaultAsync(secret => secret.Name == secretName, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var key = await GetKeyAsync(cancellationToken);
        var plain = new byte[entity.CipherText.Length];
        try
        {
            using var aes = new AesGcm(key, TAG_SIZE);
            aes.Decrypt(entity.Nonce, entity.CipherText, entity.Tag, plain, Encoding.UTF8.GetBytes(secretName));
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException ex)
        {
            throw new SyncBridgeException(_localizer.Format("Secret.Undecryptable", secretName), ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var secretName = Guard.NotBlank(name, nameof(name));
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Secrets.Where(secret => secret.Name == secretName).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Secrets.AsNoTracking().OrderBy(secret => secret.Name).Select(secret => secret.Name).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Loads or creates the data key.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The key bytes.</returns>
    /// <exception cref="SyncBridgeException">The key file exists but is malformed.</exception>
    private async Task<byte[]> GetKeyAsync(CancellationToken cancellationToken)
    {
        if (_key is not null)
        {
            return _key;
        }

        await _keyLock.WaitAsync(cancellationToken);
        try
        {
            if (_key is not null)
            {
                return _key;
            }

            var path = Path.Combine(_appDataFactory.Create().KeysDirectory, KEY_FILE_NAME);
            if (!File.Exists(path))
            {
                await FilePermissions.WriteAtomicallyAsync(path, RandomNumberGenerator.GetBytes(KEY_SIZE), ownerOnly: true, cancellationToken);
            }

            var key = await File.ReadAllBytesAsync(path, cancellationToken);
            if (key.Length != KEY_SIZE)
            {
                throw new SyncBridgeException(_localizer.Format("Secret.KeyMalformed", path, KEY_SIZE, key.Length));
            }

            _key = key;
            return key;
        }
        finally
        {
            _keyLock.Release();
        }
    }
}
