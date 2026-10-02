using RCynQ.Core.Enums;

namespace RCynQ.Sync.SyncBase;

/// <summary>
/// Records where the sync base directory came from and which of its files differ from that origin.
/// </summary>
/// <param name="Mode">Where the sync base came from.</param>
/// <param name="Branch">The branch the Git sync base follows, or <see langword="null"/> for a manual snapshot or a detached HEAD.</param>
/// <param name="Commit">The commit the Git sync base was exported from, or <see langword="null"/> for a manual snapshot.</param>
/// <param name="Exchanged">
/// The files of a Git sync base that were exchanged with the peer after the export and therefore hold what the peer has rather
/// than what the commit has: the path and the line-ending-insensitive hash of the content the peer has, or <see langword="null"/>
/// when the peer has no such file. A record is only trusted while the base still holds exactly what it says, and it is carried
/// over when the base is exported again from a newer commit.
/// </param>
/// <param name="Format">How the file was written; a file of another format is discarded and the base rebuilt from the commit.</param>
public sealed record SyncBaseMetadata(SyncBaseMode Mode, string? Branch, string? Commit, IReadOnlyDictionary<string, string?> Exchanged, int Format = 0)
{
    /// <summary>
    /// The current format. Format 3 records what the peer has for each exchanged file (a hash), so a record that does not match the
    /// base is recognized and dropped; earlier formats recorded bare paths and are not trusted.
    /// </summary>
    public const int CURRENT_FORMAT = 3;

    /// <summary>
    /// Creates the metadata of a manual snapshot.
    /// </summary>
    /// <returns>The metadata.</returns>
    public static SyncBaseMetadata Manual() => new(SyncBaseMode.Manual, null, null, new Dictionary<string, string?>(), CURRENT_FORMAT);
}
