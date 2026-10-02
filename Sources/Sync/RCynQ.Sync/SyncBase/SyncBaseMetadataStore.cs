using System.Text.Json;
using RCynQ.Core.Abstractions.Storage;

namespace RCynQ.Sync.SyncBase;

/// <summary>
/// Reads and writes <see cref="SyncBaseMetadata"/> in the data directory of the node.
/// </summary>
/// <remarks>
/// The file is written to a temporary file first and then moved into place, so a crash never leaves half a file behind.
/// A missing or unreadable file reads as <see langword="null"/>: the sync base is then treated as one of an older version
/// (a manual snapshot) and converted when a Git base becomes available.
/// </remarks>
public sealed class SyncBaseMetadataStore
{
    /// <summary>JSON options of the file.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The node data directories.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncBaseMetadataStore"/> class.
    /// </summary>
    /// <param name="appData">The node data directories.</param>
    public SyncBaseMetadataStore(IAppDataFactory appData)
    {
        _appData = appData;
    }

    /// <summary>
    /// Reads the metadata.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The metadata, or <see langword="null"/> when there is none or it cannot be read.</returns>
    public async Task<SyncBaseMetadata?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var path = _appData.Create().SyncBaseMetadataPath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var metadata = await JsonSerializer.DeserializeAsync<SyncBaseMetadata>(stream, JsonOptions, cancellationToken);
            return metadata is null || !Enum.IsDefined(metadata.Mode)
                ? null
                : metadata with { Exchanged = metadata.Exchanged ?? new Dictionary<string, string?>() };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the metadata.
    /// </summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    public async Task WriteAsync(SyncBaseMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var path = _appData.Create().SyncBaseMetadataPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// Deletes the metadata.
    /// </summary>
    public void Delete()
    {
        var path = _appData.Create().SyncBaseMetadataPath;
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
