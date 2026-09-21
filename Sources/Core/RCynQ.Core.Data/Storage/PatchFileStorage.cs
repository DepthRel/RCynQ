using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

namespace RCynQ.Core.Data.Storage;

/// <summary>
/// Stores patch payloads as files below the patches directory: <c>&lt;patches&gt;/&lt;id&gt;/patch.diff</c> and <c>bundle.zip</c>.
/// </summary>
public sealed class PatchFileStorage : IPatchFileStorage
{
    /// <summary>
    /// The diff file name.
    /// </summary>
    public const string DiffFileName = "patch.diff";

    /// <summary>
    /// The bundle file name.
    /// </summary>
    public const string BundleFileName = "bundle.zip";

    /// <summary>
    /// The application data factory.
    /// </summary>
    private readonly IAppDataFactory _appDataFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchFileStorage"/> class.
    /// </summary>
    /// <param name="appDataFactory">The application data factory.</param>
    public PatchFileStorage(IAppDataFactory appDataFactory)
    {
        _appDataFactory = appDataFactory;
    }

    /// <inheritdoc />
    public async Task SaveAsync(Guid patchId, byte[] diff, byte[] bundle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(bundle);
        var directory = GetPatchDirectory(patchId);
        Directory.CreateDirectory(directory);
        await FilePermissions.WriteAtomicallyAsync(Path.Combine(directory, DiffFileName), diff, ownerOnly: true, cancellationToken);
        await FilePermissions.WriteAtomicallyAsync(Path.Combine(directory, BundleFileName), bundle, ownerOnly: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task<byte[]> ReadDiffAsync(Guid patchId, CancellationToken cancellationToken = default)
        => ReadAsync(patchId, DiffFileName, cancellationToken);

    /// <inheritdoc />
    public Task<byte[]> ReadBundleAsync(Guid patchId, CancellationToken cancellationToken = default)
        => ReadAsync(patchId, BundleFileName, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid patchId, CancellationToken cancellationToken = default)
    {
        var directory = GetPatchDirectory(patchId);
        var exists = File.Exists(Path.Combine(directory, DiffFileName)) && File.Exists(Path.Combine(directory, BundleFileName));
        return Task.FromResult(exists);
    }

    /// <summary>
    /// Gets the directory of a patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <returns>The directory path.</returns>
    public string GetPatchDirectory(Guid patchId)
        => Path.Combine(_appDataFactory.Create().PatchesDirectory, patchId.ToString("N"));

    /// <summary>
    /// Reads one payload file.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="fileName">The payload file name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The payload bytes.</returns>
    /// <exception cref="SyncBridgeException">The payload is not stored.</exception>
    private async Task<byte[]> ReadAsync(Guid patchId, string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetPatchDirectory(patchId), fileName);
        if (!File.Exists(path))
        {
            throw new SyncBridgeException(Texts.Format("Patch.PayloadMissing", fileName, patchId));
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }
}
