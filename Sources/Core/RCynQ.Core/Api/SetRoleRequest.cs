using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// Body of <c>PUT /config/role</c>.
/// </summary>
/// <param name="Role">The new role.</param>
/// <param name="RepoPath">The repository path (Origin), or <see langword="null"/> to keep the configured value.</param>
/// <param name="WorkdirPath">The working directory (Workstation), or <see langword="null"/> to keep the configured value.</param>
public sealed record SetRoleRequest(NodeRole Role, string? RepoPath, string? WorkdirPath);
