using RCynQ.Core.Config;
using RCynQ.Core.Patches;

namespace RCynQ.Sync.Services;

/// <summary>
/// Tells which repository of the node a patch belongs to.
/// </summary>
internal static class PatchRepositoryMembership
{
    /// <summary>
    /// Tells whether a patch belongs to the repository the node works with now.
    /// </summary>
    /// <param name="repository">The repositories of the node, or <see langword="null"/> for the one it started with.</param>
    /// <param name="patch">The patch.</param>
    /// <returns><see langword="true"/> when the patch belongs to that repository, or the node has one repository only.</returns>
    public static bool Belongs(IActiveRepository? repository, PatchMetadata patch)
    {
        if (repository?.Current is not { } current || repository.Entries.Count <= 1)
        {
            return true;
        }

        // A patch without a name was exchanged before the node knew the name: it is of the repository the node started with.
        return patch.Repository is null ? current.Id == RepositoryEntry.DefaultId : current.IsNamed(patch.Repository);
    }
}
