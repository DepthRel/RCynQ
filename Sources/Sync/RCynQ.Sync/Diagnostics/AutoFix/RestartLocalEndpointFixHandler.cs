using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Sync.Diagnostics.Endpoint;

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
