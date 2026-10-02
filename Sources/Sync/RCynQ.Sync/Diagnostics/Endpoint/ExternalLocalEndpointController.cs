using RCynQ.Core.Diagnostics;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Endpoint;

/// <summary>
/// <see cref="ILocalEndpointController"/> for processes that do not host the endpoint (for example the CLI).
/// </summary>
public sealed class ExternalLocalEndpointController : ILocalEndpointController
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalLocalEndpointController"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    public ExternalLocalEndpointController(ILocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <inheritdoc />
    public Task<AutoFixResult> RestartAsync(CancellationToken cancellationToken)
        => Task.FromResult(new AutoFixResult(false, _localizer.Get("Fix.EndpointExternal")));
}
