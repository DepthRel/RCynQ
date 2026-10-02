using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Localization;
using RCynQ.Sync.Diagnostics.AutoFix;

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

    /// <summary>The registry of the fixes this node proposed, or <see langword="null"/> when nobody checks them.</summary>
    private readonly IssuedFixRegistry? _issuedFixes;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsRunner"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="checks">The registered checks.</param>
    /// <param name="node">The local node.</param>
    /// <param name="options">The node options.</param>
    /// <param name="chain">The chain repository.</param>
    /// <param name="appData">The application data factory.</param>
    /// <param name="events">The event publisher.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="runs">The run repository, or <see langword="null"/> to skip persistence.</param>
    /// <param name="issuedFixes">The registry that remembers the proposed fixes, or <see langword="null"/>.</param>
    public DiagnosticsRunner(
        ILocalizer localizer,
        IEnumerable<IDiagnosticCheck> checks,
        NodeConfig node,
        SyncBridgeOptions options,
        IChainLinkRepository chain,
        IAppDataFactory appData,
        IEventPublisher events,
        TimeProvider timeProvider,
        IDiagnosticRunRepository? runs = null,
        IssuedFixRegistry? issuedFixes = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _checks = checks.ToArray();
        _node = node;
        _options = options;
        _chain = chain;
        _appData = appData;
        _events = events;
        _timeProvider = timeProvider;
        _runs = runs;
        _issuedFixes = issuedFixes;
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
                results.Add(DiagnosticResult.Fail(check.Name, _localizer.Format("Diag.Timeout", CheckTimeout.TotalSeconds)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(DiagnosticResult.Fail(check.Name, _localizer.Format("Diag.Unexpected", _localizer.Describe(ex))));
            }
        }

        // Remembered before anybody sees the results: only a fix proposed here may be applied later.
        _issuedFixes?.Remember(results);
        var run = new DiagnosticsEvent(_node.Role, results, _timeProvider.GetUtcNow());
        if (_runs is not null)
        {
            await _runs.AddAsync(run, cancellationToken);
        }

        await _events.PublishDiagnosticsAsync(run, cancellationToken);
        return run;
    }
}
