using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// One route of a link with the outcome of its last check.
/// </summary>
/// <param name="Id">The route identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Host">The host name or address.</param>
/// <param name="SshPort">The SSH port.</param>
/// <param name="SshUser">The SSH user.</param>
/// <param name="Priority">The preference; lower values are preferred.</param>
/// <param name="IsActive">Whether the traffic to the peer goes this way now.</param>
/// <param name="Reachability">Whether the peer answered at the last check.</param>
/// <param name="LatencyMilliseconds">How long the answer took at the last check, or <see langword="null"/>.</param>
/// <param name="CheckedAtUtc">The time of the last check, or <see langword="null"/> when there was none.</param>
public sealed record LinkRouteDto(
    Guid Id,
    string Name,
    string Host,
    int SshPort,
    string SshUser,
    int Priority,
    bool IsActive,
    RouteReachability Reachability,
    double? LatencyMilliseconds,
    DateTimeOffset? CheckedAtUtc);
