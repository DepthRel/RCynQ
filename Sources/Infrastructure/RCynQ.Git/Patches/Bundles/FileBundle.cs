namespace RCynQ.Git.Patches.Bundles;

/// <summary>
/// The decoded content of a file bundle.
/// </summary>
/// <param name="Files">Regular file contents by normalized relative path.</param>
/// <param name="Symlinks">Symbolic link targets by normalized relative path.</param>
/// <param name="Deleted">Normalized relative paths deleted by the patch.</param>
public sealed record FileBundle(
    IReadOnlyDictionary<string, byte[]> Files,
    IReadOnlyDictionary<string, string> Symlinks,
    IReadOnlySet<string> Deleted)
{
    /// <summary>
    /// Gets the total number of entries.
    /// </summary>
    public int Count => Files.Count + Symlinks.Count + Deleted.Count;
}
