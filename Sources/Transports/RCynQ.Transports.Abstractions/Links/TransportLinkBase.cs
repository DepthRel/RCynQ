using RCynQ.Core.Abstractions.Transports;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Localization;

namespace RCynQ.Transports.Abstractions.Links;

/// <summary>
/// Shared state handling and error translation for transport links.
/// </summary>
/// <remarks>
/// <see cref="StartAsync"/> runs the transport specific start and then probes the link; a link that is not usable after
/// starting raises <see cref="SyncBridgeException"/>, so the link manager can fail over. Unexpected exceptions are
/// translated to <see cref="SyncBridgeException"/> and recorded in <see cref="CurrentState"/>.
/// </remarks>
public abstract class TransportLinkBase : ITransportLink
{
    /// <summary>
    /// Guards <see cref="_state"/>.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// The last observed state.
    /// </summary>
    private LinkState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportLinkBase"/> class.
    /// </summary>
    /// <param name="config">The link configuration.</param>
    /// <param name="kind">The transport kind.</param>
    /// <param name="timeProvider">The clock.</param>
    protected TransportLinkBase(ChainLinkConfig config, TransportKind kind, TimeProvider timeProvider)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Kind = kind;
        TimeProvider = timeProvider;
        _state = CreateState(LinkStatus.Unknown, null);
    }

    /// <inheritdoc />
    public TransportKind Kind { get; }

    /// <summary>
    /// Gets the link configuration.
    /// </summary>
    public ChainLinkConfig Config { get; }

    /// <summary>
    /// Gets the last observed state.
    /// </summary>
    public LinkState CurrentState
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Gets the clock.
    /// </summary>
    protected TimeProvider TimeProvider { get; }

    /// <summary>
    /// Gets the stable diagnostic check name of this link.
    /// </summary>
    protected string CheckName => $"Transport:{Config.Name}:{Kind}";

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        SetState(CreateState(LinkStatus.Starting, "Starting."));
        LinkState probed;
        try
        {
            await StartCoreAsync(cancellationToken);
            probed = await ProbeCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SetState(CreateState(LinkStatus.Failed, Texts.Get("Transport.StartCancelled")));
            throw;
        }
        catch (Exception ex)
        {
            SetState(CreateState(LinkStatus.Failed, ex.Message));
            throw ex as SyncBridgeException ?? new SyncBridgeException(Texts.Format("Transport.StartFailed", Config.Name, Kind, ex.Message), ex);
        }

        SetState(probed);
        if (!probed.IsUsable)
        {
            throw new SyncBridgeException(Texts.Format("Transport.NotUsable", Config.Name, Kind, probed.Message));
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await StopCoreAsync(cancellationToken);
        }
        finally
        {
            SetState(CreateState(LinkStatus.Stopped, "Stopped."));
        }
    }

    /// <inheritdoc />
    public async Task<LinkState> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        LinkState state;
        try
        {
            state = await ProbeCoreAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            state = CreateState(LinkStatus.Failed, ex.Message);
        }

        SetState(state);
        return state;
    }

    /// <inheritdoc />
    public async Task<DiagnosticResult> DiagnoseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await DiagnoseCoreAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DiagnosticResult.Fail(CheckName, ex.Message);
        }
    }

    /// <summary>
    /// Performs the transport specific start.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the transport was started.</returns>
    protected abstract Task StartCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Performs the transport specific stop.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the transport was stopped.</returns>
    protected abstract Task StopCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Determines the current state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The state.</returns>
    protected abstract Task<LinkState> ProbeCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs transport specific diagnostics.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result.</returns>
    protected abstract Task<DiagnosticResult> DiagnoseCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a state for this link.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="message">The message.</param>
    /// <returns>The state.</returns>
    protected LinkState CreateState(LinkStatus status, string? message)
        => new(Config.Id, Config.Name, Kind, status, message, TimeProvider.GetUtcNow());

    /// <summary>
    /// Stores the state.
    /// </summary>
    /// <param name="state">The state.</param>
    private void SetState(LinkState state)
    {
        lock (_gate)
        {
            _state = state;
        }
    }
}
