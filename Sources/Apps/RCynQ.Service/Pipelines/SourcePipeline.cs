using Microsoft.Extensions.Logging;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Sync.Paths;
using RCynQ.Sync.State;

namespace RCynQ.Service.Pipelines;

/// <summary>
/// Source pipeline: patch send and receive, Git fetch through Dest and events. On start it records the current state.
/// </summary>
public sealed class SourcePipeline : IRolePipeline
{
    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The logger.</summary>
    private readonly ILogger<SourcePipeline> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourcePipeline"/> class.
    /// </summary>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="logger">The logger.</param>
    public SourcePipeline(StateTracker tracker, SyncPaths paths, ILogger<SourcePipeline> logger)
    {
        _tracker = tracker;
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public NodeRole Role => NodeRole.Source;

    /// <inheritdoc />
    public RoleCapabilities Capabilities => RoleCapabilities.For(Role);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var state = await _tracker.RefreshAsync(cancellationToken);
        _logger.LogInformation("Source pipeline started for {Workdir}; baseline {Baseline}, local state {Hash}.",
            _paths.Workdir, _paths.BaselineInitialized ? "initialized" : "missing", state.LocalStateHash);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
