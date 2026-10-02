using System.Net;
using System.Security.Cryptography;
using System.Text;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Localization;

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
    /// <param name="localizer">The localizer that words the problems for the reader.</param>
    /// <returns>The list of human-readable problems; empty when the options are valid.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static IReadOnlyList<string> Validate(SyncBridgeOptions options, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(localizer);
        var errors = new List<string>();

        var repositoryErrors = ValidateRepositories(options, localizer);
        errors.AddRange(repositoryErrors);
        if (repositoryErrors.Count == 0)
        {
            TryAdd(errors, localizer, () => ToNodeConfig(options), "Node");
        }

        if (options.ListenPort is < 1 or > 65535)
        {
            errors.Add(localizer.Format("Config.ListenPortRange", options.ListenPort));
        }

        if (options.Endpoint.Port is < 1 or > 65535)
        {
            errors.Add(localizer.Format("Config.EndpointPortRange", options.Endpoint.Port));
        }

        if (options.Endpoint.Mode == EndpointMode.Direct)
        {
            if (options.Role == NodeRole.Relay)
            {
                errors.Add(localizer.Get("Config.RelayNeedsSsh"));
            }

            if (options.Endpoint.Port == options.ListenPort)
            {
                errors.Add(localizer.Get("Config.PortsMustDiffer"));
            }

            if (!IPAddress.TryParse(options.Endpoint.BindAddress, out _))
            {
                errors.Add(localizer.Format("Config.BindAddressInvalid", options.Endpoint.BindAddress));
            }
        }

        if (options.PatchMaxAgeMinutes < 1)
        {
            errors.Add(localizer.Get("Config.PatchMaxAge"));
        }

        if (options.RetentionDays < 0)
        {
            errors.Add(localizer.Get("Config.RetentionDays"));
        }

        if (options.Ssh.ConnectTimeoutSeconds < 1)
        {
            errors.Add(localizer.Get("Config.SshTimeout"));
        }

        if (!string.IsNullOrWhiteSpace(options.Ssh.ConfigFile) &&
            (options.Ssh.ConfigFile.Any(char.IsControl) || !SshClientOptions.IsValidConfigFileName(Path.GetFileName(options.Ssh.ConfigFile))))
        {
            errors.Add(localizer.Format("Config.SshConfigFileName", options.Ssh.ConfigFile));
        }

        var ids = new HashSet<Guid>();
        for (var i = 0; i < options.Chain.Count; i++)
        {
            var index = i;
            ChainLinkConfig? link = null;
            TryAdd(errors, localizer, () => link = ToChainLink(options.Chain[index]), $"Chain[{index}]");
            if (link is not null && !ids.Add(link.Id))
            {
                errors.Add(localizer.Format("Config.DuplicateLinkId", index, link.Id));
            }
        }

        if (errors.Count == 0 && options.Role is NodeRole.Workstation or NodeRole.Origin && options.Chain.Count > 0)
        {
            var chain = ToChainLinks(options);
            var endpoint = chain[^1];
            var expected = options.Role == NodeRole.Workstation ? NodeRole.Origin : NodeRole.Workstation;
            if (endpoint.Peer.Role != expected)
            {
                errors.Add(localizer.Format("Config.LastLinkRole", expected, endpoint.Name, endpoint.Peer.Role));
            }

            foreach (var hop in chain.Take(chain.Count - 1))
            {
                if (hop.Peer.Role != NodeRole.Relay)
                {
                    errors.Add(localizer.Format("Config.IntermediateLinkRole", hop.Name, hop.Peer.Role));
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
        var path = ActiveRepositoryOf(options)?.Path;
        return new NodeConfig(id, name, options.Role, options.Role == NodeRole.Origin ? path : null, options.Role == NodeRole.Workstation ? path : null);
    }

    /// <summary>
    /// Maps the configured repositories to domain records.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The repositories in the order of the configuration.</returns>
    /// <exception cref="ArgumentException">A name is not valid or a path is blank.</exception>
    public static IReadOnlyList<RepositoryEntry> ToRepositories(SyncBridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Repositories
            .Select(repository => new RepositoryEntry(repository.Id, (repository.Name ?? string.Empty).Trim(), (repository.Path ?? string.Empty).Trim()))
            .ToArray();
    }

    /// <summary>
    /// Finds the repository the node works with: the one named by <see cref="SyncBridgeOptions.ActiveRepository"/>, or
    /// the first one when no name is given.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The repository, or <see langword="null"/> when the node has none.</returns>
    /// <exception cref="ArgumentException">A repository is not valid.</exception>
    public static RepositoryEntry? ActiveRepositoryOf(SyncBridgeOptions options)
    {
        var repositories = ToRepositories(options);
        var name = options.ActiveRepository?.Trim();
        return (string.IsNullOrEmpty(name) ? null : repositories.FirstOrDefault(repository => repository.IsNamed(name)))
            ?? repositories.FirstOrDefault();
    }

    /// <summary>
    /// Validates the repositories of the node.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <returns>The problems found; empty when the repositories are valid.</returns>
    private static List<string> ValidateRepositories(SyncBridgeOptions options, ILocalizer localizer)
    {
        var errors = new List<string>();
        if (options.Role == NodeRole.Relay)
        {
            if (options.Repositories.Count > 0)
            {
                errors.Add(localizer.Get("Node.RelayNoPaths"));
            }

            return errors;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<Guid>();
        var unbound = 0;
        foreach (var repository in options.Repositories)
        {
            var name = (repository.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                unbound++;
            }
            else if (!RepositoryName.IsValid(name))
            {
                errors.Add(localizer.Format("Config.RepositoryName", name));
            }
            else if (!names.Add(name))
            {
                errors.Add(localizer.Format("Config.RepositoryDuplicate", name));
            }

            if (string.IsNullOrWhiteSpace(repository.Path))
            {
                errors.Add(localizer.Format("Config.RepositoryPath", name));
            }

            if (!ids.Add(repository.Id))
            {
                errors.Add(localizer.Format("Config.RepositoryDuplicateId", repository.Id));
            }
        }

        // A folder without a name is the working copy of a Workstation before its first contact with the Origin.
        if (unbound > (options.Role == NodeRole.Workstation ? 1 : 0))
        {
            errors.Add(localizer.Get("Config.RepositoryUnbound"));
        }

        var active = options.ActiveRepository?.Trim();
        if (!string.IsNullOrEmpty(active) && !names.Contains(active))
        {
            errors.Add(localizer.Format("Config.RepositoryActiveUnknown", active));
        }

        return errors;
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
            Normalize(link.SigningPublicKey));

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
            link.TransportSettings,
            ToRoutes(link, peer));
    }

    /// <summary>
    /// Maps the routes written for a link: the address of the link itself and the other ways to its peer.
    /// </summary>
    /// <param name="link">The link options.</param>
    /// <param name="peer">The peer of the link, with the address of the link.</param>
    /// <returns>The routes; empty when the link names no other address than its own.</returns>
    /// <exception cref="ArgumentException">A route is invalid.</exception>
    private static IReadOnlyList<LinkRoute> ToRoutes(ChainLinkOptions link, PeerDescriptor peer)
    {
        if (link.Routes is not { Count: > 0 })
        {
            return Array.Empty<LinkRoute>();
        }

        var routes = new List<LinkRoute> { Route(link.Name, peer.Host, peer.Ssh, link.RoutePriority) };
        foreach (var route in link.Routes)
        {
            var address = new SshEndpoint(Normalize(route.SshUser) ?? peer.SshUser, route.Host, route.SshPort ?? peer.SshPort);
            routes.Add(Route(link.Name, Normalize(route.Name) ?? address.Host, address, route.Priority));
        }

        return routes;
    }

    /// <summary>
    /// Creates a route whose identifier follows from its link and address, so a route written in the configuration
    /// keeps its identity between starts.
    /// </summary>
    /// <param name="linkName">The name of the link.</param>
    /// <param name="name">The display name of the route.</param>
    /// <param name="address">The address.</param>
    /// <param name="priority">The preference.</param>
    /// <returns>The route.</returns>
    private static LinkRoute Route(string linkName, string name, SshEndpoint address, int priority)
        => new(StableId($"{linkName}/route/{address.JumpSpec.ToLowerInvariant()}"), name, address, priority);

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
    /// <param name="localizer">The localizer that words the problems.</param>
    /// <param name="action">The action to run.</param>
    /// <param name="scope">The scope prefix for messages.</param>
    private static void TryAdd(List<string> errors, ILocalizer localizer, Action action, string scope)
    {
        try
        {
            action();
        }
        catch (ArgumentException ex)
        {
            errors.Add($"{scope}: {localizer.Describe(ex)}");
        }
    }

    /// <summary>
    /// Converts blank strings to <see langword="null"/>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The trimmed value or <see langword="null"/>.</returns>
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
