using Microsoft.Extensions.Logging;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Sync.Diagnostics.AutoFix;
using RCynQ.Sync.State;

namespace RCynQ.Service.Pipelines;

/// <summary>
/// Dest pipeline: patch receive and apply, git-over-ssh serving and Git diagnostics published on start.
/// </summary>
public sealed class DestPipeline : IRolePipeline
{
    /// <summary>
    /// The checks run on start.
    /// </summary>
    public static readonly IReadOnlySet<string> StartupChecks = new HashSet<string> { "GitInstalled", "GitVersion", "RepoValid" };

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The registered checks.</summary>
    private readonly IEnumerable<IDiagnosticCheck> _checks;

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>The chain repository.</summary>
    private readonly IChainLinkRepository _chain;

    /// <summary>The application data factory.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>The event publisher.</summary>
    private readonly IEventPublisher _events;

    /// <summary>The logger.</summary>
    private readonly ILogger<DestPipeline> _logger;

    /// <summary>The registry of the fixes this node proposed.</summary>
    private readonly IssuedFixRegistry _issuedFixes;

    /// <summary>
    /// Initializes a new instance of the <see cref="DestPipeline"/> class.
    /// </summary>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="checks">The registered checks.</param>
    /// <param name="node">The local node.</param>
    /// <param name="options">The node options.</param>
    /// <param name="chain">The chain repository.</param>
    /// <param name="appData">The application data factory.</param>
    /// <param name="events">The event publisher.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="issuedFixes">The registry of the fixes this node proposed.</param>
    public DestPipeline(
        StateTracker tracker,
        IEnumerable<IDiagnosticCheck> checks,
        NodeConfig node,
        SyncBridgeOptions options,
        IChainLinkRepository chain,
        IAppDataFactory appData,
        IEventPublisher events,
        ILogger<DestPipeline> logger,
        IssuedFixRegistry issuedFixes)
    {
        _tracker = tracker;
        _checks = checks;
        _node = node;
        _options = options;
        _chain = chain;
        _appData = appData;
        _events = events;
        _logger = logger;
        _issuedFixes = issuedFixes;
    }

    /// <inheritdoc />
    public NodeRole Role => NodeRole.Dest;

    /// <inheritdoc />
    public RoleCapabilities Capabilities => RoleCapabilities.For(Role);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _tracker.RefreshAsync(cancellationToken);
        var context = new DiagnosticContext(_node, _options, await _chain.GetAllAsync(cancellationToken), _appData.Create());
        var results = new List<DiagnosticResult>();
        foreach (var check in _checks.Where(check => StartupChecks.Contains(check.Name)))
        {
            results.Add(await check.RunAsync(context, cancellationToken));
        }

        _issuedFixes.Remember(results);
        await _events.PublishDiagnosticsAsync(new DiagnosticsEvent(Role, results, DateTimeOffset.UtcNow), cancellationToken);
        foreach (var failed in results.Where(result => !result.Passed))
        {
            _logger.LogWarning("Dest start check {Check} failed: {Message} {Fix}", failed.CheckName, failed.Message, failed.SuggestedFix);
        }

        _logger.LogInformation("Dest pipeline started for repository {Repo}.", _node.RepoPath);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
