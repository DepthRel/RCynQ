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

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceLocalEndpointController"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="probe">The endpoint probe.</param>
    /// <param name="options">The node options.</param>
    public ServiceLocalEndpointController(ILocalizer localizer, ILocalEndpointProbe probe, SyncBridgeOptions options)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _probe = probe;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<AutoFixResult> RestartAsync(CancellationToken cancellationToken)
        => await _probe.IsHealthyAsync(_options.ListenPort, cancellationToken)
            ? new AutoFixResult(true, _localizer.Format("Fix.EndpointRunning", _options.ListenPort))
            : new AutoFixResult(false, _localizer.Get("Fix.EndpointNotAnswering"));
}
