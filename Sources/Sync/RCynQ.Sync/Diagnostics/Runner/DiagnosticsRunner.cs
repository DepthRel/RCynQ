using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Ssh.Keys;
using RCynQ.Transports.Abstractions;

namespace RCynQ.Sync.Diagnostics.Runner;

/// <summary>
/// Default <see cref="IDiagnosticsRunner"/>.
/// </summary>
/// <remarks>
/// Each check runs with its own timeout; a crashing or timed out check becomes a failed result, so one broken check never
/// hides the others.
/// </remarks>
public sealed class DiagnosticsRunner : IDiagnosticsRunner
{
    /// <summary>The per-check timeout.</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The registered checks.</summary>
    private readonly IReadOnlyList<IDiagnosticCheck> _checks;

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

    /// <summary>The run repository.</summary>
    private readonly IDiagnosticRunRepository? _runs;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsRunner"/> class.
    /// </summary>
    /// <param name="checks">The registered checks.</param>
    /// <param name="node">The local node.</param>
    /// <param name="options">The node options.</param>
    /// <param name="chain">The chain repository.</param>
    /// <param name="appData">The application data factory.</param>
    /// <param name="events">The event publisher.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="runs">The run repository, or <see langword="null"/> to skip persistence.</param>
    public DiagnosticsRunner(
        IEnumerable<IDiagnosticCheck> checks,
        NodeConfig node,
        SyncBridgeOptions options,
        IChainLinkRepository chain,
        IAppDataFactory appData,
        IEventPublisher events,
        TimeProvider timeProvider,
        IDiagnosticRunRepository? runs = null)
    {
        _checks = checks.ToArray();
        _node = node;
        _options = options;
        _chain = chain;
        _appData = appData;
        _events = events;
        _timeProvider = timeProvider;
        _runs = runs;
    }

    /// <inheritdoc />
    public IReadOnlyList<IDiagnosticCheck> GetApplicableChecks(NodeRole role)
        => _checks.Where(check => check.Roles.Contains(role)).ToArray();

    /// <inheritdoc />
    public async Task<DiagnosticsEvent> RunAsync(CancellationToken cancellationToken = default)
    {
        var context = new DiagnosticContext(_node, _options, await _chain.GetAllAsync(cancellationToken), _appData.Create());
        var results = new List<DiagnosticResult>();
        foreach (var check in GetApplicableChecks(_node.Role))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);
            try
            {
                results.Add(await check.RunAsync(context, timeout.Token));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                results.Add(DiagnosticResult.Fail(check.Name, $"The check timed out after {CheckTimeout.TotalSeconds:0} s."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(DiagnosticResult.Fail(check.Name, $"The check failed unexpectedly: {ex.Message}"));
            }
        }

        var run = new DiagnosticsEvent(_node.Role, results, _timeProvider.GetUtcNow());
        if (_runs is not null)
        {
            await _runs.AddAsync(run, cancellationToken);
        }

        await _events.PublishDiagnosticsAsync(run, cancellationToken);
        return run;
    }
}
