using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Patches;

/// <summary>
/// Computes deterministic state hashes from file entries.
/// </summary>
/// <remarks>
/// The hash must be deterministic for the same set of entries regardless of
/// platform or build, so it can be compared across nodes.
/// </remarks>
public interface IStateHasher
{
    /// <summary>
    /// Computes the deterministic state hash for the given file entries.
    /// </summary>
    /// <param name="entries">
    /// The file entries describing the state. Must not be <see langword="null"/>;
    /// an empty list yields the hash of an empty state.
    /// </param>
    /// <returns>The deterministic state hash string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    string ComputeHash(IReadOnlyList<FileEntry> entries);
}
