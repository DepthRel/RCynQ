using System.Net;
using System.Security.Cryptography;
using System.Text;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;

namespace RCynQ.Core.Config;

/// <summary>
/// Validates <see cref="SyncBridgeOptions"/> and maps them to domain records.
/// </summary>
/// <remarks>
/// Validation collects every problem instead of stopping at the first one so the user gets a complete,
/// actionable report.
/// </remarks>
public static class SyncBridgeOptionsValidator
{
    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    /// <returns>The list of human-readable problems; empty when the options are valid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<string> Validate(SyncBridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        TryAdd(errors, () => ToNodeConfig(options), "Node");

        if (options.ListenPort is < 1 or > 65535)
        {
            errors.Add($"ListenPort {options.ListenPort} must be in range 1..65535.");
        }

        if (options.Endpoint.Port is < 1 or > 65535)
        {
            errors.Add($"Endpoint.Port {options.Endpoint.Port} must be in range 1..65535.");
        }

        if (options.Endpoint.Mode == EndpointMode.Direct)
        {
            if (options.Role == NodeRole.Mediator)
            {
                errors.Add("A Mediator relays its neighbours through its SSH server, so Endpoint.Mode must be SshTunnel.");
            }

            if (options.Endpoint.Port == options.ListenPort)
            {
                errors.Add("Endpoint.Port must differ from ListenPort: the local API keeps its own loopback port.");
            }

            if (!IPAddress.TryParse(options.Endpoint.BindAddress, out _))
            {
                errors.Add($"Endpoint.BindAddress '{options.Endpoint.BindAddress}' is not a valid IP address.");
            }
        }

        if (options.PatchMaxAgeMinutes < 1)
        {
            errors.Add("PatchMaxAgeMinutes must be positive.");
        }

        if (options.Ssh.ConnectTimeoutSeconds < 1)
        {
            errors.Add("Ssh.ConnectTimeoutSeconds must be positive.");
        }

        var ids = new HashSet<Guid>();
        for (var i = 0; i < options.Chain.Count; i++)
        {
            var index = i;
            ChainLinkConfig? link = null;
            TryAdd(errors, () => link = ToChainLink(options.Chain[index]), $"Chain[{index}]");
            if (link is not null && !ids.Add(link.Id))
            {
                errors.Add($"Chain[{index}]: duplicate link identifier {link.Id}.");
            }
        }

        if (errors.Count == 0 && options.Role is NodeRole.Source or NodeRole.Dest && options.Chain.Count > 0)
        {
            var chain = ToChainLinks(options);
            var endpoint = chain[^1];
            var expected = options.Role == NodeRole.Source ? NodeRole.Dest : NodeRole.Source;
            if (endpoint.Peer.Role != expected)
            {
                errors.Add($"The last chain link must point to a {expected} peer, but '{endpoint.Name}' points to {endpoint.Peer.Role}.");
            }

            foreach (var hop in chain.Take(chain.Count - 1))
            {
                if (hop.Peer.Role != NodeRole.Mediator)
                {
                    errors.Add($"Intermediate chain link '{hop.Name}' must point to a Mediator peer, but points to {hop.Peer.Role}.");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Maps the options to the local node configuration.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The node configuration.</returns>
    /// <exception cref="ArgumentException">The options do not describe a valid node.</exception>
    public static NodeConfig ToNodeConfig(SyncBridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var id = NodeId.Parse(options.NodeId);
        var name = string.IsNullOrWhiteSpace(options.Name) ? id.Value : options.Name;
        return new NodeConfig(id, name, options.Role, Normalize(options.RepoPath), Normalize(options.WorkdirPath));
    }

    /// <summary>
    /// Maps all configured chain links, ordered by priority.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The ordered chain links.</returns>
    /// <exception cref="ArgumentException">A link is invalid.</exception>
    public static IReadOnlyList<ChainLinkConfig> ToChainLinks(SyncBridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Chain.Select(ToChainLink).OrderBy(link => link.Priority).ThenBy(link => link.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Maps one chain link option to its domain record.
    /// </summary>
    /// <param name="link">The link options.</param>
    /// <returns>The link configuration.</returns>
    /// <exception cref="ArgumentException">The link is invalid.</exception>
    public static ChainLinkConfig ToChainLink(ChainLinkOptions link)
    {
        ArgumentNullException.ThrowIfNull(link);
        var peerId = NodeId.Parse(link.PeerNodeId);
        var peer = new PeerDescriptor(
            peerId,
            string.IsNullOrWhiteSpace(link.PeerName) ? peerId.Value : link.PeerName,
            link.PeerRole,
            link.Host,
            link.SshPort,
            link.SshUser,
            Normalize(link.HostKeyFingerprint),
            Normalize(link.SigningPublicKey),
            Normalize(link.GitRepoPath));

        var id = link.Id is { } explicitId && explicitId != Guid.Empty ? explicitId : StableId(link.Name);
        return new ChainLinkConfig(
            id,
            link.Name,
            link.Transport,
            peer,
            link.LocalPort,
            link.RemotePort,
            link.Priority,
            link.FallbackTransports,
            link.TransportSettings);
    }

    /// <summary>
    /// Derives a deterministic identifier from a link name so configuration-defined links keep their identity.
    /// </summary>
    /// <param name="name">The link name.</param>
    /// <returns>The derived identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public static Guid StableId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Link name must not be blank.", nameof(name));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("rcynq-link:" + name.Trim()));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// Runs a mapping action and records its argument errors.
    /// </summary>
    /// <param name="errors">The error collection.</param>
    /// <param name="action">The action to run.</param>
    /// <param name="scope">The scope prefix for messages.</param>
    private static void TryAdd(List<string> errors, Action action, string scope)
    {
        try
        {
            action();
        }
        catch (ArgumentException ex)
        {
            errors.Add($"{scope}: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts blank strings to <see langword="null"/>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The trimmed value or <see langword="null"/>.</returns>
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
