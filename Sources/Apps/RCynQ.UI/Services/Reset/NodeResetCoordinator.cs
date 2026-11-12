using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Localization;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Hosting;

namespace RCynQ.UI.Services.Reset;

/// <summary>
/// Default <see cref="INodeResetCoordinator"/>.
/// </summary>
public sealed class NodeResetCoordinator : INodeResetCoordinator
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The service API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The controller of the local service.</summary>
    private readonly IServiceController _controller;

    /// <summary>Removes the configuration and the data.</summary>
    private readonly INodeDataEraser _eraser;

    /// <summary>Finds the root of a portable installation.</summary>
    private readonly Func<string?> _portableRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeResetCoordinator"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="api">The service API client.</param>
    /// <param name="controller">The controller of the local service.</param>
    /// <param name="eraser">Removes the configuration and the data.</param>
    /// <param name="portableRoot">Finds the root of a portable installation; <see langword="null"/> looks next to the application.</param>
    public NodeResetCoordinator(ILocalizer localizer, IServiceApiClient api, IServiceController controller, INodeDataEraser eraser, Func<string?>? portableRoot = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _eraser = eraser ?? throw new ArgumentNullException(nameof(eraser));
        _portableRoot = portableRoot ?? (() => ConfigLocations.FindPortableRoot());
    }

    /// <inheritdoc />
    public bool CanReset => _controller.IsManaged;

    /// <inheritdoc />
    public NodeResetPlan? Plan()
        => LocalConfigFile.TryLoad(_controller.ConfigPath) is { } options
            ? _eraser.Plan(options, _controller.ConfigPath, _portableRoot())
            : null;

    /// <inheritdoc />
    public async Task<NodeResetOutcome> ResetAsync(NodeResetPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // The service knows the neighbours and the invitations of the node, so it takes their lines out while it runs.
        string? access;
        try
        {
            var revocation = await _api.RevokePeerAccessAsync(cancellationToken);
            access = revocation.NeedsElevation
                ? _localizer.Format("Reset.AccessNeedsAdmin", revocation.FilePath)
                : revocation.Removed > 0 ? _localizer.Format("Reset.AccessRemoved", revocation.Removed, revocation.FilePath) : null;
        }
        catch (Exception ex) when (ex is ServiceApiException or HttpRequestException)
        {
            access = _localizer.Format("Reset.AccessFailed", _localizer.Describe(ex));
        }

        // The service holds the database open: it ends before its files go.
        await _controller.StopAsync(cancellationToken);
        return new NodeResetOutcome(_eraser.Erase(plan), access);
    }
}
