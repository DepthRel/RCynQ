using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.AuthorizedKeys;

namespace RCynQ.Sync.Configuration.Access;

/// <summary>
/// Decides which neighbours may connect to this node and what exactly each key may do.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Origin: a Workstation key may only forward to the local RCynQ endpoint and runs no command.</item>
/// <item>Workstation: an Origin key may only forward to the local RCynQ endpoint.</item>
/// <item>Relay: a Workstation key may only forward to the SSH servers of the Origin peers and an Origin key only to the SSH
/// servers of the Workstation peers. Either key may also forward to the SSH servers of the neighbouring Relays, which is
/// how a chain passes more than one Relay. A neighbour known at several addresses is allowed at each of them.</item>
/// </list>
/// Every line is restricted: no shell, no terminal, no agent or X11 forwarding.
/// </remarks>
public static class PeerAccessPlanner
{
    /// <summary>
    /// Builds the lines for the neighbours.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="listenPort">The port of the local RCynQ endpoint.</param>
    /// <param name="peers">The imported neighbours.</param>
    /// <param name="links">
    /// The links of the node, whose routes name the other addresses a neighbour is reachable at; <see langword="null"/>
    /// when every neighbour has the one address of its description.
    /// </param>
    /// <returns>The plan.</returns>
    public static PeerAccessPlan Plan(NodeConfig node, int listenPort, IReadOnlyList<PeerDescriptor> peers, IReadOnlyList<ChainLinkConfig>? links = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(peers);
        links ??= Array.Empty<ChainLinkConfig>();

        var entries = new List<PlannedAccess>();
        var missing = new List<PeerDescriptor>();
        var waiting = new List<PeerDescriptor>();
        var endpoint = $"127.0.0.1:{listenPort.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        foreach (var peer in peers.OrderBy(peer => peer.Id.Value, StringComparer.Ordinal))
        {
            IReadOnlyList<string> targets;
            switch (node.Role)
            {
                case NodeRole.Origin when peer.Role == NodeRole.Workstation:
                    targets = new[] { endpoint };
                    break;
                case NodeRole.Workstation when peer.Role == NodeRole.Origin:
                    targets = new[] { endpoint };
                    break;
                case NodeRole.Relay when peer.Role is NodeRole.Workstation or NodeRole.Origin:
                    var opposite = peer.Role == NodeRole.Workstation ? NodeRole.Origin : NodeRole.Workstation;
                    // The opposite endpoint is reached on its SSH server, or on its own TLS port when it runs none. The
                    // way to it may lead through one more Relay, whose SSH server is then the next stop. Every address
                    // a neighbour is known at is allowed, so the endpoints can change the route without this node.
                    targets = peers.Where(other => other.Role == opposite || (other.Role == NodeRole.Relay && other.Id != node.Id))
                        .SelectMany(other => TargetsOf(other, links))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    break;
                default:
                    continue;
            }

            var key = TryParseKey(peer.SshPublicKey);
            if (key is null)
            {
                missing.Add(peer);
                continue;
            }

            if (targets.Count == 0)
            {
                waiting.Add(peer);
                continue;
            }

            entries.Add(new PlannedAccess(peer, AuthorizedKeysManager.BuildForwardLine(key, targets, $"rcynq:{peer.Id.Value}")));
        }

        return new PeerAccessPlan(entries, missing, waiting);
    }

    /// <summary>
    /// Lists the <c>permitopen</c> targets of a neighbour: its own address and the address of every route to it.
    /// </summary>
    /// <param name="peer">The neighbour.</param>
    /// <param name="links">The links of the node.</param>
    /// <returns>The targets; the same one may be listed twice.</returns>
    private static IEnumerable<string> TargetsOf(PeerDescriptor peer, IReadOnlyList<ChainLinkConfig> links)
    {
        yield return FormatTarget(peer.Host, peer.ReachablePort);
        foreach (var route in links.Where(link => link.Peer.Id == peer.Id).SelectMany(link => link.Routes))
        {
            // A neighbour without an SSH server listens on its TLS port at every address.
            yield return FormatTarget(route.Ssh.Host, peer.EndpointMode == EndpointMode.Direct ? peer.EndpointPort : route.Ssh.Port);
        }
    }

    /// <summary>
    /// Formats a <c>permitopen</c> target, bracketing IPv6 addresses.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="port">The port.</param>
    /// <returns>The target.</returns>
    public static string FormatTarget(string host, int port)
    {
        var text = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return host.Contains(':') ? $"[{host}]:{text}" : $"{host}:{text}";
    }

    /// <summary>
    /// Parses a stored key.
    /// </summary>
    /// <param name="line">The key line.</param>
    /// <returns>The key, or <see langword="null"/> when missing or invalid.</returns>
    private static SshPublicKey? TryParseKey(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return SshPublicKey.Parse(line);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
