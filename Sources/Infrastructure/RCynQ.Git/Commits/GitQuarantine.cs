using RCynQ.Core.Api;

namespace RCynQ.Git.Commits;

/// <summary>
/// The objects of a received bundle, kept in a folder of their own beside the repository until they are taken or thrown away.
/// </summary>
/// <param name="Directory">The object directory of the quarantine.</param>
/// <param name="RepositoryObjects">The object directory of the repository the quarantine belongs to.</param>
/// <param name="Heads">The references the bundle carries, with their commits.</param>
public sealed record GitQuarantine(string Directory, string RepositoryObjects, IReadOnlyList<GitBranchTip> Heads)
{
    /// <summary>
    /// Gets the environment that makes Git read the objects of the repository and of the quarantine while it writes only
    /// into the quarantine.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GIT_OBJECT_DIRECTORY"] = Directory,
        ["GIT_ALTERNATE_OBJECT_DIRECTORIES"] = RepositoryObjects
    };
}
