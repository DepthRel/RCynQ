using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Sync.Diagnostics.Endpoint;
using RCynQ.Localization;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// <see cref="ILocalEndpointController"/> of the process that hosts the endpoint.
/// </summary>
/// <remarks>
/// Kestrel cannot be restarted inside a running host without dropping in-flight work, so the controller only verifies
/// that the endpoint answers and otherwise asks for a process restart through the service manager.
/// </remarks>
public sealed class ServiceLocalEndpointController : ILocalEndpointController
{
    /// <summary>The endpoint probe.</summary>
    private readonly ILocalEndpointProbe _probe;

    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceLocalEndpointController"/> class.
    /// </summary>
    /// <param name="probe">The endpoint probe.</param>
    /// <param name="options">The node options.</param>
    public ServiceLocalEndpointController(ILocalEndpointProbe probe, SyncBridgeOptions options)
    {
        _probe = probe;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<AutoFixResult> RestartAsync(CancellationToken cancellationToken)
        => await _probe.IsHealthyAsync(_options.ListenPort, cancellationToken)
            ? new AutoFixResult(true, Texts.Format("Fix.EndpointRunning", _options.ListenPort))
            : new AutoFixResult(false, Texts.Get("Fix.EndpointNotAnswering"));
}
