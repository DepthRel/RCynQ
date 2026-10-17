using Microsoft.AspNetCore.SignalR.Client;
using RCynQ.Core.Api;
using RCynQ.Core.Events;
using RCynQ.UI.Services.Connection;

namespace RCynQ.UI.Services.Events;

/// <summary>
/// <see cref="INodeEventStream"/> backed by a SignalR connection to <c>/hub</c>.
/// </summary>
/// <remarks>
/// The connection is built on the first <see cref="ConnectAsync"/> so that it uses the token available at that moment
/// (the service may have created the token file after the UI started).
/// </remarks>
public sealed class SignalRNodeEventStream : INodeEventStream
{
    /// <summary>The connection settings.</summary>
    private readonly ServiceConnectionSettings _settings;

    /// <summary>The token provider.</summary>
    private readonly ApiTokenProvider _tokens;

    /// <summary>The hub connection, or <see langword="null"/> before the first connect.</summary>
    private HubConnection? _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRNodeEventStream"/> class.
    /// </summary>
    /// <param name="settings">The connection settings.</param>
    /// <param name="tokens">The token provider, or <see langword="null"/> to create one from <paramref name="settings"/>.</param>
    public SignalRNodeEventStream(ServiceConnectionSettings settings, ApiTokenProvider? tokens = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _tokens = tokens ?? new ApiTokenProvider(settings);
    }

    /// <inheritdoc />
    public event EventHandler<LinkStateChangedEvent>? LinkStateChanged;

    /// <inheritdoc />
    public event EventHandler<PatchEvent>? PatchChanged;

    /// <inheritdoc />
    public event EventHandler<DiagnosticsEvent>? DiagnosticsCompleted;

    /// <inheritdoc />
    public event EventHandler<RepositoryEvent>? RepositoryChanged;

    /// <inheritdoc />
    public event EventHandler<LinkRoutesChangedEvent>? LinkRoutesChanged;

    /// <inheritdoc />
    public event EventHandler<JoinProgressEvent>? JoinProgress;

    /// <inheritdoc />
    public event EventHandler<bool>? ConnectionChanged;

    /// <inheritdoc />
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { State: not HubConnectionState.Disconnected })
        {
            return;
        }

        if (_connection is not null)
        {
            // A connection that failed or was closed keeps the token it was built with; rebuild with the current one.
            await _connection.DisposeAsync();
        }

        _connection = Build(_tokens.GetToken());
        await _connection.StartAsync(cancellationToken);
        await Notify(true);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _connection?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// Builds a hub connection.
    /// </summary>
    /// <param name="token">The API token.</param>
    /// <returns>The connection.</returns>
    private HubConnection Build(string token)
    {
        var hub = new Uri(_settings.ServiceUri, ApiRoutes.HUB.TrimStart('/'));
        var connection = new HubConnectionBuilder()
            .WithUrl(hub, options =>
            {
                if (token.Length > 0)
                {
                    options.Headers[ApiHeaders.API_TOKEN] = token;
                }

                options.HttpMessageHandlerFactory = _ => new SocketsHttpHandler { UseProxy = false };
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .Build();

        connection.On<LinkStateChangedEvent>(NodeEventNames.LINK_STATE_CHANGED, evt => LinkStateChanged?.Invoke(this, evt));
        connection.On<PatchEvent>(NodeEventNames.PATCH_EVENT, evt => PatchChanged?.Invoke(this, evt));
        connection.On<DiagnosticsEvent>(NodeEventNames.DIAGNOSTICS_EVENT, evt => DiagnosticsCompleted?.Invoke(this, evt));
        connection.On<RepositoryEvent>(NodeEventNames.REPOSITORY_EVENT, evt => RepositoryChanged?.Invoke(this, evt));
        connection.On<LinkRoutesChangedEvent>(NodeEventNames.LINK_ROUTES_CHANGED, evt => LinkRoutesChanged?.Invoke(this, evt));
        connection.On<JoinProgressEvent>(NodeEventNames.JOIN_PROGRESS, evt => JoinProgress?.Invoke(this, evt));
        connection.Reconnecting += _ => Notify(false);
        connection.Reconnected += _ => Notify(true);
        connection.Closed += _ => Notify(false);
        return connection;
    }

    /// <summary>
    /// Raises <see cref="ConnectionChanged"/>.
    /// </summary>
    /// <param name="connected">Whether the stream is connected.</param>
    /// <returns>A completed task.</returns>
    private Task Notify(bool connected)
    {
        ConnectionChanged?.Invoke(this, connected);
        return Task.CompletedTask;
    }
}
