using RCynQ.Core.Diagnostics;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Endpoint;

/// <summary>
/// <see cref="ILocalEndpointController"/> for processes that do not host the endpoint (for example the CLI).
/// </summary>
public sealed class ExternalLocalEndpointController : ILocalEndpointController
{
    /// <inheritdoc />
    public Task<AutoFixResult> RestartAsync(CancellationToken cancellationToken)
        => Task.FromResult(new AutoFixResult(false, Texts.Get("Fix.EndpointExternal")));
}
