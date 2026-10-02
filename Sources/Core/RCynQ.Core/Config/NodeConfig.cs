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
///   <item><description>Source requires <see cref="WorkdirPath"/> (the local working copy).</description></item>
///   <item><description>Dest requires <see cref="RepoPath"/>; the working directory defaults to the repository.</description></item>
///   <item><description>Mediator has no repository and no working directory.</description></item>
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
    /// <param name="repoPath">The Git repository path (required for Dest, forbidden for Mediator).</param>
    /// <param name="workdirPath">The working directory path (required for Source, forbidden for Mediator).</param>
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
            case NodeRole.Source when WorkdirPath is null:
                throw LocalizedError.Argument(nameof(workdirPath), "Node.SourceNeedsWorkdir");
            case NodeRole.Dest when RepoPath is null:
                throw LocalizedError.Argument(nameof(repoPath), "Node.DestNeedsRepo");
            case NodeRole.Mediator when RepoPath is not null || WorkdirPath is not null:
                throw LocalizedError.Argument(nameof(role), "Node.MediatorNoPaths");
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
    /// <see cref="RepoPath"/>. <see langword="null"/> for a Mediator.
    /// </summary>
    public string? EffectiveWorkdir => WorkdirPath ?? RepoPath;

    /// <summary>
    /// Gets a value indicating whether the role takes part in the live patch channel.
    /// </summary>
    public bool ParticipatesInPatchChannel => Role is NodeRole.Source or NodeRole.Dest;
}
