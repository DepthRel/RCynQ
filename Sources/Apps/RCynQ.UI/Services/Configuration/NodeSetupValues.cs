using RCynQ.Core.Enums;

namespace RCynQ.UI.Services.Configuration;

/// <summary>
/// The values the application writes into <c>syncbridge.json</c>.
/// </summary>
/// <param name="NodeId">The node identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Role">The node role.</param>
/// <param name="Path">The repository path (Dest) or working copy (Source); ignored for a Mediator.</param>
/// <param name="ListenPort">The loopback port of the local service.</param>
/// <param name="LinkToken">The shared token of Source and Dest; ignored for a Mediator.</param>
/// <param name="EndpointMode">How neighbours reach this node.</param>
/// <param name="EndpointPort">The TCP port of the direct endpoint.</param>
/// <param name="EndpointBindAddress">The address the direct endpoint binds to.</param>
public sealed record NodeSetupValues(
    string NodeId,
    string Name,
    NodeRole Role,
    string Path,
    int ListenPort,
    string LinkToken,
    EndpointMode EndpointMode = EndpointMode.SshTunnel,
    int EndpointPort = 18444,
    string EndpointBindAddress = "0.0.0.0");
