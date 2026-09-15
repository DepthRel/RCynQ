using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Ssh.Keys;
using RCynQ.Sync.Diagnostics.Endpoint;
using RCynQ.Transports.Abstractions;

namespace RCynQ.Sync.Diagnostics.AutoFix;

/// <summary>
/// Restarts the local endpoint through <see cref="ILocalEndpointController"/>.
/// </summary>
public sealed class RestartLocalEndpointFixHandler : IAutoFixHandler
{
    /// <summary>The controller.</summary>
    private readonly ILocalEndpointController _controller;

    /// <summary>
    /// Initializes a new instance of the <see cref="RestartLocalEndpointFixHandler"/> class.
    /// </summary>
    /// <param name="controller">The controller.</param>
    public RestartLocalEndpointFixHandler(ILocalEndpointController controller)
    {
        _controller = controller;
    }

    /// <inheritdoc />
    public DiagnosticFixKind Kind => DiagnosticFixKind.RestartLocalEndpoint;

    /// <inheritdoc />
    public Task<AutoFixResult> ExecuteAsync(DiagnosticFix fix, CancellationToken cancellationToken) => _controller.RestartAsync(cancellationToken);
}
