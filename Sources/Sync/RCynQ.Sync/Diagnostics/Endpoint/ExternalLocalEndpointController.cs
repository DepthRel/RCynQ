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
using RCynQ.Transports.Abstractions;

namespace RCynQ.Sync.Diagnostics.Endpoint;

/// <summary>
/// <see cref="ILocalEndpointController"/> for processes that do not host the endpoint (for example the CLI).
/// </summary>
public sealed class ExternalLocalEndpointController : ILocalEndpointController
{
    /// <inheritdoc />
    public Task<AutoFixResult> RestartAsync(CancellationToken cancellationToken)
        => Task.FromResult(new AutoFixResult(false, "The endpoint is hosted by RCynQ.Service; start or restart that process with your service manager."));
}
