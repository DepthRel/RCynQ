using RCynQ.Core.Enums;
using RCynQ.Core.Validation;

namespace RCynQ.Core.Config;

/// <summary>
/// Describes one link of the chain from the local node towards the counterpart endpoint.
/// </summary>
/// <remarks>
/// <para>
/// On Source and Dest the chain is ordered by <see cref="Priority"/>: every link except the last
/// one points at a Mediator used as SSH jump host, and the last link points at the counterpart
/// endpoint whose loopback patch endpoint listens on <see cref="RemotePort"/>.
/// </para>
/// <para>
/// On a Mediator the chain lists the neighbours whose transports the mediator must keep up.
/// </para>
/// </remarks>
public sealed record ChainLinkConfig
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChainLinkConfig"/> record.
    /// </summary>
    /// <param name="id">The unique link identifier.</param>
    /// <param name="name">The display name of the link.</param>
    /// <param name="transport">The primary transport of the link.</param>
    /// <param name="peer">The peer at the far end of the link.</param>
    /// <param name="localPort">
    /// The local TCP port used for the SSH local forward to the endpoint, or <c>0</c> for an ephemeral port.
    /// </param>
    /// <param name="remotePort">
    /// For the endpoint link, the loopback port of the peer's patch endpoint; for mediator links, the
    /// port the mediator may open towards its next hop (usually <c>22</c>).
    /// </param>
    /// <param name="priority">The position of the link in the chain; lower values come first.</param>
    /// <param name="fallbackTransports">Transports tried in order when the primary transport fails.</param>
    /// <param name="transportSettings">Transport specific settings, for example <c>configPath</c> for AmneziaWG.</param>
    /// <exception cref="ArgumentNullException"><paramref name="peer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The name is blank or a transport is not defined.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A port or the priority is out of range.</exception>
    public ChainLinkConfig(
        Guid id,
        string name,
        TransportKind transport,
        PeerDescriptor peer,
        int localPort,
        int remotePort,
        int priority = 0,
        IReadOnlyList<TransportKind>? fallbackTransports = null,
        IReadOnlyDictionary<string, string>? transportSettings = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Link identifier must not be empty.", nameof(id));
        }

        Id = id;
        Name = Guard.NotBlank(name, nameof(name));
        Transport = Guard.Defined(transport, nameof(transport));
        Peer = Guard.NotNull(peer, nameof(peer));
        LocalPort = Guard.Port(localPort, nameof(localPort), allowZero: true);
        RemotePort = Guard.Port(remotePort, nameof(remotePort));
        Priority = Guard.NonNegative(priority, nameof(priority));
        FallbackTransports = (fallbackTransports ?? Array.Empty<TransportKind>())
            .Select(kind => Guard.Defined(kind, nameof(fallbackTransports)))
            .Where(kind => kind != transport)
            .Distinct()
            .ToArray();
        TransportSettings = transportSettings is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(transportSettings, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the unique link identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the display name of the link.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the primary transport of the link.
    /// </summary>
    public TransportKind Transport { get; }

    /// <summary>
    /// Gets the peer at the far end of the link.
    /// </summary>
    public PeerDescriptor Peer { get; }

    /// <summary>
    /// Gets the local TCP port for the SSH local forward, or <c>0</c> for an ephemeral port.
    /// </summary>
    public int LocalPort { get; }

    /// <summary>
    /// Gets the remote TCP port reached through the link.
    /// </summary>
    public int RemotePort { get; }

    /// <summary>
    /// Gets the position of the link in the chain; lower values come first.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Gets the transports tried in order when the primary transport fails. Never contains the primary transport.
    /// </summary>
    public IReadOnlyList<TransportKind> FallbackTransports { get; }

    /// <summary>
    /// Gets the transport specific settings (case-insensitive keys).
    /// </summary>
    public IReadOnlyDictionary<string, string> TransportSettings { get; }

    /// <summary>
    /// Creates a copy of this link that uses a different primary transport.
    /// </summary>
    /// <param name="transport">The transport to use.</param>
    /// <returns>A new link configuration with the same identity and settings.</returns>
    public ChainLinkConfig WithTransport(TransportKind transport)
        => new(Id, Name, transport, Peer, LocalPort, RemotePort, Priority, FallbackTransports, TransportSettings);

    /// <summary>
    /// Creates a copy of this link that leads to another description of the peer.
    /// </summary>
    /// <param name="peer">The peer description to use.</param>
    /// <param name="remotePort">The remote port to use, or <see langword="null"/> to keep the current one.</param>
    /// <returns>A new link configuration with the same identity and settings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="peer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="remotePort"/> is outside 1..65535.</exception>
    public ChainLinkConfig WithPeer(PeerDescriptor peer, int? remotePort = null)
        => new(Id, Name, Transport, peer, LocalPort, remotePort ?? RemotePort, Priority, FallbackTransports, TransportSettings);
}
