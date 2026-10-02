using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Validation;
using RCynQ.Localization;

namespace RCynQ.Core.Config;

/// <summary>
/// Describes the configuration of the local RCynQ node.
/// </summary>
/// <remarks>
/// Path requirements depend on the role:
/// <list type="bullet">
///   <item><description>Workstation requires <see cref="WorkdirPath"/> (the local working copy).</description></item>
///   <item><description>Origin requires <see cref="RepoPath"/>; the working directory defaults to the repository.</description></item>
///   <item><description>Relay has no repository and no working directory.</description></item>
/// </list>
/// </remarks>
public sealed record NodeConfig
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NodeConfig"/> record.
    /// </summary>
    /// <param name="id">The node identifier.</param>
    /// <param name="name">The display name of the node.</param>
    /// <param name="role">The role of the node.</param>
    /// <param name="repoPath">The Git repository path (required for Origin, forbidden for Relay).</param>
    /// <param name="workdirPath">The working directory path (required for Workstation, forbidden for Relay).</param>
    /// <exception cref="ArgumentException">A value is blank, the identifier is empty or the paths do not fit the role.</exception>
    public NodeConfig(NodeId id, string name, NodeRole role, string? repoPath, string? workdirPath)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("Node identifier must not be empty.", nameof(id));
        }

        Id = id;
        Name = Guard.NotBlank(name, nameof(name));
        Role = Guard.Defined(role, nameof(role));
        RepoPath = Guard.OptionalNotBlank(repoPath, nameof(repoPath));
        WorkdirPath = Guard.OptionalNotBlank(workdirPath, nameof(workdirPath));

        switch (Role)
        {
            case NodeRole.Workstation when WorkdirPath is null:
                throw LocalizedError.Argument(nameof(workdirPath), "Node.WorkstationNeedsWorkdir");
            case NodeRole.Origin when RepoPath is null:
                throw LocalizedError.Argument(nameof(repoPath), "Node.OriginNeedsRepo");
            case NodeRole.Relay when RepoPath is not null || WorkdirPath is not null:
                throw LocalizedError.Argument(nameof(role), "Node.RelayNoPaths");
        }
    }

    /// <summary>
    /// Gets the node identifier.
    /// </summary>
    public NodeId Id { get; }

    /// <summary>
    /// Gets the display name of the node.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the role of the node.
    /// </summary>
    public NodeRole Role { get; }

    /// <summary>
    /// Gets the Git repository path, or <see langword="null"/>.
    /// </summary>
    public string? RepoPath { get; }

    /// <summary>
    /// Gets the configured working directory path, or <see langword="null"/>.
    /// </summary>
    public string? WorkdirPath { get; }

    /// <summary>
    /// Gets the working tree live patches are applied to: <see cref="WorkdirPath"/> when set, otherwise
    /// <see cref="RepoPath"/>. <see langword="null"/> for a Relay.
    /// </summary>
    public string? EffectiveWorkdir => WorkdirPath ?? RepoPath;

    /// <summary>
    /// Gets a value indicating whether the role takes part in the live patch channel.
    /// </summary>
    public bool ParticipatesInPatchChannel => Role is NodeRole.Workstation or NodeRole.Origin;

    /// <summary>
    /// Returns the node with the paths of a repository, for code that describes the node as it works now.
    /// </summary>
    /// <remarks>
    /// The paths of a <see cref="NodeConfig"/> are those of the repository the node started with. The repository it works
    /// with now is told by <see cref="IActiveRepository"/>.
    /// </remarks>
    /// <param name="repository">The repository, or <see langword="null"/> to keep the paths.</param>
    /// <returns>The node; a Relay is returned unchanged.</returns>
    public NodeConfig WithRepository(RepositoryEntry? repository)
        => repository is null || Role == NodeRole.Relay
            ? this
            : new NodeConfig(Id, Name, Role, Role == NodeRole.Origin ? repository.Path : null, Role == NodeRole.Workstation ? repository.Path : null);
}
