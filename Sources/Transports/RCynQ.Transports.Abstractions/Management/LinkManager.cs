using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Transports;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Transports.Abstractions.Factory;

namespace RCynQ.Transports.Abstractions.Management;

/// <summary>
/// Default <see cref="ILinkManager"/>.
/// </summary>
/// <remarks>
/// Failover tries <see cref="ChainLinkConfig.Transport"/> first and then <see cref="ChainLinkConfig.FallbackTransports"/>
/// in order. A link running on a fallback transport is reported as <see cref="LinkStatus.Degraded"/> with the reasons the
/// preferred transports failed. State changes are published through <see cref="IEventPublisher"/>.
/// </remarks>
public sealed class LinkManager : ILinkManager, IAsyncDisposable
{
    /// <summary>The link repository.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>The link factory.</summary>
    private readonly TransportLinkFactory _factory;

    /// <summary>The event publisher.</summary>
    private readonly IEventPublisher _events;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Active links by identifier.</summary>
    private readonly ConcurrentDictionary<Guid, ActiveLink> _active = new();

    /// <summary>Last published states by identifier.</summary>
    private readonly ConcurrentDictionary<Guid, LinkState> _published = new();

    /// <summary>Serializes start and stop operations.</summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkManager"/> class.
    /// </summary>
    /// <param name="links">The link repository.</param>
    /// <param name="factory">The link factory.</param>
    /// <param name="events">The event publisher.</param>
    /// <param name="timeProvider">The clock.</param>
    public LinkManager(IChainLinkRepository links, TransportLinkFactory factory, IEventPublisher events, TimeProvider timeProvider)
    {
        _links = links;
        _factory = factory;
        _events = events;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LinkState>> StartAllAsync(CancellationToken cancellationToken = default)
    {
        var states = new List<LinkState>();
        foreach (var config in await _links.GetAllAsync(cancellationToken))
        {
            states.Add(await StartAsync(config.Id, cancellationToken));
        }

        return states;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LinkState>> StopAllAsync(CancellationToken cancellationToken = default)
    {
        var states = new List<LinkState>();
        foreach (var id in _active.Keys.ToArray())
        {
            states.Add(await StopAsync(id, cancellationToken));
        }

        return states;
    }

    /// <inheritdoc />
    public async Task<LinkState> StartAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(linkId, cancellationToken);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_active.TryRemove(linkId, out var previous))
            {
                await StopQuietlyAsync(previous.Link);
            }

            return await StartWithFailoverAsync(config, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<LinkState> StopAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(linkId, cancellationToken);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var kind = config.Transport;
            if (_active.TryRemove(linkId, out var active))
            {
                kind = active.Kind;
                await active.Link.StopAsync(cancellationToken);
            }
            else if (_factory.IsSupported(config.Transport))
            {
                await _factory.Create(config).StopAsync(cancellationToken);
            }

            var state = new LinkState(config.Id, config.Name, kind, LinkStatus.Stopped, "Stopped.", _timeProvider.GetUtcNow());
            await PublishIfChangedAsync(state, cancellationToken);
            return state;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LinkState>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var states = new List<LinkState>();
        foreach (var config in await _links.GetAllAsync(cancellationToken))
        {
            LinkState state;
            if (_active.TryGetValue(config.Id, out var active))
            {
                state = await active.Link.GetStatusAsync(cancellationToken);
                if (active.Kind != config.Transport && state.Status == LinkStatus.Ready)
                {
                    state = new LinkState(config.Id, config.Name, active.Kind, LinkStatus.Degraded, $"Using fallback {active.Kind}. {state.Message}", state.UpdatedAtUtc);
                }
            }
            else if (_factory.IsSupported(config.Transport))
            {
                state = await _factory.Create(config).GetStatusAsync(cancellationToken);
            }
            else
            {
                state = new LinkState(config.Id, config.Name, config.Transport, LinkStatus.Failed, $"Transport {config.Transport} is not available in this build.", _timeProvider.GetUtcNow());
            }

            states.Add(state);
            await PublishIfChangedAsync(state, cancellationToken);
        }

        return states;
    }

    /// <inheritdoc />
    public async Task<LinkState> TryFailoverAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(linkId, cancellationToken);
        if (_active.TryGetValue(linkId, out var active) && active.Kind == config.Transport)
        {
            var state = await active.Link.GetStatusAsync(cancellationToken);
            if (state.IsUsable)
            {
                return state;
            }
        }

        return await StartAsync(linkId, cancellationToken);
    }

    /// <summary>
    /// Stops all active links.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (var id in _active.Keys.ToArray())
        {
            if (_active.TryRemove(id, out var active))
            {
                await StopQuietlyAsync(active.Link);
            }
        }

        _lock.Dispose();
    }

    /// <summary>
    /// Starts a link trying the primary and fallback transports.
    /// </summary>
    /// <param name="config">The link configuration.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resulting state.</returns>
    private async Task<LinkState> StartWithFailoverAsync(ChainLinkConfig config, CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var kind in new[] { config.Transport }.Concat(config.FallbackTransports))
        {
            if (!_factory.IsSupported(kind))
            {
                failures.Add($"{kind}: not available in this build");
                continue;
            }

            var link = _factory.Create(config.WithTransport(kind));
            try
            {
                await link.StartAsync(cancellationToken);
                var state = await link.GetStatusAsync(cancellationToken);
                if (!state.IsUsable)
                {
                    throw new SyncBridgeException(state.Message ?? "Link is not usable.");
                }

                _active[config.Id] = new ActiveLink(link, kind);
                if (kind != config.Transport)
                {
                    state = new LinkState(config.Id, config.Name, kind, LinkStatus.Degraded, $"Using fallback {kind}; {string.Join("; ", failures)}", _timeProvider.GetUtcNow());
                }

                await PublishIfChangedAsync(state, cancellationToken);
                return state;
            }
            catch (SyncBridgeException ex)
            {
                failures.Add($"{kind}: {ex.Message}");
                await StopQuietlyAsync(link);
            }
        }

        var failed = new LinkState(config.Id, config.Name, config.Transport, LinkStatus.Failed, string.Join("; ", failures), _timeProvider.GetUtcNow());
        await PublishIfChangedAsync(failed, cancellationToken);
        return failed;
    }

    /// <summary>
    /// Loads a link configuration.
    /// </summary>
    /// <param name="linkId">The link identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="KeyNotFoundException">The link is not configured.</exception>
    private async Task<ChainLinkConfig> GetConfigAsync(Guid linkId, CancellationToken cancellationToken)
        => await _links.GetAsync(linkId, cancellationToken) ?? throw new KeyNotFoundException($"Chain link {linkId} is not configured.");

    /// <summary>
    /// Publishes a state when its status or transport changed since the last publication. Message-only changes are not
    /// published, so periodic probing does not flood clients with identical states.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task PublishIfChangedAsync(LinkState state, CancellationToken cancellationToken)
    {
        if (_published.TryGetValue(state.LinkId, out var previous)
            && previous.Status == state.Status && previous.Kind == state.Kind)
        {
            return;
        }

        _published[state.LinkId] = state;
        await _events.PublishLinkStateAsync(new LinkStateChangedEvent(state), cancellationToken);
    }

    /// <summary>
    /// Stops a link, ignoring failures.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <returns>A task representing the operation.</returns>
    private static async Task StopQuietlyAsync(ITransportLink link)
    {
        try
        {
            await link.StopAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is SyncBridgeException or InvalidOperationException or IOException)
        {
            // The link failed already; stopping is best effort.
        }
    }

    /// <summary>
    /// An active link and the transport it runs on.
    /// </summary>
    /// <param name="Link">The link.</param>
    /// <param name="Kind">The transport kind.</param>
    private sealed record ActiveLink(ITransportLink Link, TransportKind Kind);
}
