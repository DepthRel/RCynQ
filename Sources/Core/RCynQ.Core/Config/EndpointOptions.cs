using RCynQ.Core.Enums;

namespace RCynQ.Core.Config;

/// <summary>
/// Settings of the node's patch endpoint as neighbours reach it.
/// </summary>
/// <remarks>
/// In <see cref="EndpointMode.SshTunnel"/> these settings are unused: the endpoint stays on loopback behind sshd.
/// In <see cref="EndpointMode.Direct"/> a second listener is opened on <see cref="BindAddress"/> and
/// <see cref="Port"/> with the node's own TLS certificate; the local API keeps its loopback-only port.
/// </remarks>
public sealed class EndpointOptions
{
    /// <summary>
    /// Gets or sets how neighbours reach this node.
    /// </summary>
    public EndpointMode Mode { get; set; } = EndpointMode.SshTunnel;

    /// <summary>
    /// Gets or sets the address the direct endpoint binds to; <c>0.0.0.0</c> means every interface.
    /// </summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>
    /// Gets or sets the TCP port of the direct endpoint. It must differ from <see cref="SyncBridgeOptions.ListenPort"/>.
    /// </summary>
    public int Port { get; set; } = 18444;
}
