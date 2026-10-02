using RCynQ.Core.Validation;

namespace RCynQ.Core.Config;

/// <summary>
/// One of the addresses the peer of a chain link is reachable at.
/// </summary>
/// <remarks>
/// <para>
/// A node may be reachable in more than one way: through a tunnel over UDP and through another one over TCP, by a
/// private and by a public address. Every such way is a route. The routes of a link lead to the same node, so the keys
/// and fingerprints of the peer are the same for all of them; only the address differs.
/// </para>
/// <para>
/// A link that has no routes has one address, the one of its peer, and behaves as it did before routes existed.
/// </para>
/// </remarks>
public sealed record LinkRoute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinkRoute"/> record.
    /// </summary>
    /// <param name="id">The unique route identifier.</param>
    /// <param name="name">The display name of the route.</param>
    /// <param name="ssh">The address of the peer on this route.</param>
    /// <param name="priority">The preference of the route; lower values are preferred.</param>
    /// <exception cref="ArgumentException">The identifier is empty or the name is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="ssh"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="priority"/> is negative.</exception>
    public LinkRoute(Guid id, string name, SshEndpoint ssh, int priority = 0)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Route identifier must not be empty.", nameof(id));
        }

        Id = id;
        Name = Guard.NotBlank(name, nameof(name));
        Ssh = Guard.NotNull(ssh, nameof(ssh));
        Priority = Guard.NonNegative(priority, nameof(priority));
    }

    /// <summary>
    /// Gets the unique route identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the display name of the route.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the address of the peer on this route.
    /// </summary>
    public SshEndpoint Ssh { get; }

    /// <summary>
    /// Gets the preference of the route; lower values are preferred, equal values are decided by the latency.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Tells whether the route leads to the given address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when host, port and user are the same.</returns>
    public bool LeadsTo(SshEndpoint address) => SameAddress(Ssh, address);

    /// <summary>
    /// Creates a copy of the route with another preference.
    /// </summary>
    /// <param name="priority">The preference.</param>
    /// <returns>The route.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="priority"/> is negative.</exception>
    public LinkRoute WithPriority(int priority) => new(Id, Name, Ssh, priority);

    /// <summary>
    /// Creates a copy of the route that leads to another address.
    /// </summary>
    /// <param name="ssh">The address.</param>
    /// <returns>The route.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ssh"/> is <see langword="null"/>.</exception>
    public LinkRoute WithAddress(SshEndpoint ssh) => new(Id, Name, ssh, Priority);

    /// <summary>
    /// Compares two addresses the way a route is told from another: host names without regard to case.
    /// </summary>
    /// <param name="left">The first address.</param>
    /// <param name="right">The second address.</param>
    /// <returns><see langword="true"/> when both name the same host, port and user.</returns>
    public static bool SameAddress(SshEndpoint? left, SshEndpoint? right)
        => left is not null &&
            right is not null &&
            left.Port == right.Port &&
            string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.User, right.User, StringComparison.Ordinal);
}
