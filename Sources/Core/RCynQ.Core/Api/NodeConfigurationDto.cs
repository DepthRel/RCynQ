using RCynQ.Core.Enums;
using RCynQ.Core.Ids;

namespace RCynQ.Core.Api;

/// <summary>
/// The local node configuration as reported by <c>GET /config</c>.
/// </summary>
/// <param name="NodeId">The node identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Role">The role the service runs with.</param>
/// <param name="RepoPath">The repository path, or <see langword="null"/>.</param>
/// <param name="WorkdirPath">The working directory path, or <see langword="null"/>.</param>
/// <param name="ListenPort">The loopback port of the service.</param>
/// <param name="Capabilities">The capabilities of the role.</param>
/// <param name="ConfigFilePath">The configuration file the service reads, or <see langword="null"/>.</param>
/// <param name="AcceptCommits">Whether an Origin takes commits from the Workstation; <see langword="false"/> for other roles.</param>
public sealed record NodeConfigurationDto(
    NodeId NodeId,
    string Name,
    NodeRole Role,
    string? RepoPath,
    string? WorkdirPath,
    int ListenPort,
    RoleCapabilities Capabilities,
    string? ConfigFilePath,
    bool AcceptCommits = false);
