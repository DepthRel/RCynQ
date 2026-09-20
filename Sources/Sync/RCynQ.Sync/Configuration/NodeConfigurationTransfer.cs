using System.Security.Cryptography;
using System.Text.Json;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Validation;
using RCynQ.Ssh.Keys;
using RCynQ.Sync.Endpoint;

namespace RCynQ.Sync.Configuration;

/// <summary>
/// Exports the public configuration of the local node and imports the configuration of peers.
/// </summary>
/// <remarks>
/// Import is the explicit trust decision of the pairing: the peer's signing key becomes accepted for patch verification.
/// When the peer is new and reachable, a chain link is created in the right position: mediators before the endpoint.
/// </remarks>
public sealed class NodeConfigurationTransfer
{
    /// <summary>
    /// The priority assigned to endpoint links so mediators come first.
    /// </summary>
    public const int EndpointPriority = 1000;

    /// <summary>
    /// JSON options of the export file.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>The signature service.</summary>
    private readonly ISignatureService _signatures;

    /// <summary>The peer repository.</summary>
    private readonly IPeerRepository _peers;

    /// <summary>The link repository.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeConfigurationTransfer"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="options">The node options.</param>
    /// <param name="signatures">The signature service.</param>
    /// <param name="peers">The peer repository.</param>
    /// <param name="links">The link repository.</param>
    /// <param name="timeProvider">The clock.</param>
    public NodeConfigurationTransfer(
        NodeConfig node,
        SyncBridgeOptions options,
        ISignatureService signatures,
        IPeerRepository peers,
        IChainLinkRepository links,
        TimeProvider timeProvider,
        Func<string?>? tlsFingerprint = null)
    {
        _node = node;
        _options = options;
        _signatures = signatures;
        _peers = peers;
        _links = links;
        _timeProvider = timeProvider;
        _tlsFingerprint = tlsFingerprint ?? (() => null);
    }

    /// <summary>
    /// Reads the fingerprint of this node's TLS certificate, or <see langword="null"/> when it does not exist.
    /// </summary>
    private readonly Func<string?> _tlsFingerprint;

    /// <summary>
    /// Exports the public configuration of the local node.
    /// </summary>
    /// <param name="host">The host at which neighbours reach this node's sshd, or <see langword="null"/>.</param>
    /// <param name="sshUser">The SSH user neighbours log in with, or <see langword="null"/>.</param>
    /// <param name="sshPort">The SSH port.</param>
    /// <param name="sshPublicKey">This node's SSH public key line, or <see langword="null"/>.</param>
    /// <param name="hostKeyFingerprint">The SHA-256 fingerprint of this node's sshd host key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The exported configuration.</returns>
    /// <exception cref="ArgumentException">A value is invalid.</exception>
    public async Task<ExportedNodeConfiguration> ExportAsync(string? host, string? sshUser, int sshPort = 22, string? sshPublicKey = null, string? hostKeyFingerprint = null, CancellationToken cancellationToken = default)
    {
        var normalizedFingerprint = NormalizeHostKeyFingerprint(hostKeyFingerprint)
            ?? (string.IsNullOrWhiteSpace(hostKeyFingerprint) ? null : throw new ArgumentException($"Host key fingerprint '{hostKeyFingerprint}' is not a SHA256 fingerprint.", nameof(hostKeyFingerprint)));

        if (host is not null && !SshEndpoint.IsValidHost(host))
        {
            throw new ArgumentException($"Host '{host}' is invalid.", nameof(host));
        }

        if (sshUser is not null && !SshEndpoint.IsValidUser(sshUser))
        {
            throw new ArgumentException($"SSH user '{sshUser}' is invalid.", nameof(sshUser));
        }

        Guard.Port(sshPort, nameof(sshPort));
        string? normalizedSshKey = null;
        if (!string.IsNullOrWhiteSpace(sshPublicKey))
        {
            try
            {
                normalizedSshKey = SshPublicKey.Parse(sshPublicKey).ToLine();
            }
            catch (FormatException ex)
            {
                throw new ArgumentException(ex.Message, nameof(sshPublicKey), ex);
            }
        }

        var signingKey = _node.ParticipatesInPatchChannel
            ? Convert.ToBase64String(await _signatures.ExportPublicKeyAsync(cancellationToken))
            : null;

        return new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CurrentFormatVersion,
            _node.Id,
            _node.Name,
            _node.Role,
            signingKey,
            host,
            sshUser,
            sshPort,
            _node.Role == NodeRole.Dest ? _node.RepoPath : null,
            _options.ListenPort,
            normalizedSshKey,
            _timeProvider.GetUtcNow(),
            normalizedFingerprint,
            _options.Endpoint.Mode,
            _options.Endpoint.Mode == EndpointMode.Direct ? _options.Endpoint.Port : 0,
            _options.Endpoint.Mode == EndpointMode.Direct ? EndpointCertificateFingerprint() : null);
    }

    /// <summary>
    /// Imports the configuration of a peer.
    /// </summary>
    /// <param name="imported">The peer configuration.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The import result.</returns>
    /// <exception cref="FormatException">The configuration is invalid or incomplete.</exception>
    /// <summary>
    /// Gets the fingerprint of the node's TLS certificate for the export.
    /// </summary>
    /// <returns>The fingerprint.</returns>
    /// <exception cref="SyncBridgeException">The certificate does not exist yet.</exception>
    private string EndpointCertificateFingerprint()
        => _tlsFingerprint()
            ?? throw new SyncBridgeException(
                "This node accepts connections on its own port, but its TLS certificate has not been created yet. Start the service once and export again.");

    public async Task<ImportResult> ImportAsync(ExportedNodeConfiguration imported, CancellationToken cancellationToken = default)
    {
        Validate(imported);
        if (imported.NodeId == _node.Id)
        {
            throw new FormatException("The file describes this node itself; import the peer's export instead.");
        }

        var existing = await _peers.GetAsync(imported.NodeId, cancellationToken);
        var host = imported.Host ?? existing?.Host
            ?? throw new FormatException($"The export of {imported.NodeId} has no host; export it again with a host.");
        var user = imported.SshUser ?? existing?.SshUser
            ?? throw new FormatException($"The export of {imported.NodeId} has no SSH user; export it again with a user.");
        var port = imported.SshPort > 0 ? imported.SshPort : existing?.SshPort ?? 22;

        PeerDescriptor peer;
        try
        {
            peer = new PeerDescriptor(
                imported.NodeId, imported.Name, imported.Role, host, port, user,
                NormalizeHostKeyFingerprint(imported.HostKeyFingerprint) ?? existing?.HostKeyFingerprint,
                imported.SigningPublicKey ?? existing?.SigningPublicKey,
                imported.GitRepoPath ?? existing?.GitRepoPath,
                imported.SshPublicKey is null ? existing?.SshPublicKey : SshPublicKey.Parse(imported.SshPublicKey).ToLine(),
                imported.EndpointMode,
                imported.TlsFingerprint is null
                    ? imported.EndpointMode == existing?.EndpointMode ? existing.TlsFingerprint : null
                    : NodeCertificateStore.NormalizeFingerprint(imported.TlsFingerprint),
                imported.EndpointMode == EndpointMode.Direct ? imported.EndpointPort : 0);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"The export of {imported.NodeId} is invalid: {ex.Message}", ex);
        }

        await _peers.UpsertAsync(peer, cancellationToken);
        var links = await _links.GetAllAsync(cancellationToken);
        var linked = links.Where(link => link.Peer.Id == peer.Id).ToList();
        foreach (var link in linked)
        {
            await _links.UpsertAsync(
                new ChainLinkConfig(link.Id, link.Name, link.Transport, peer, link.LocalPort, link.RemotePort, link.Priority, link.FallbackTransports, link.TransportSettings),
                cancellationToken);
        }

        if (linked.Count > 0)
        {
            return new ImportResult(peer.Id, peer.Role, false, linked[0].Id, $"Updated peer {peer.Id} and {linked.Count} link(s).");
        }

        var counterpart = _node.Role switch
        {
            NodeRole.Source => NodeRole.Dest,
            NodeRole.Dest => NodeRole.Source,
            _ => (NodeRole?)null
        };

        int priority;
        int remotePort;
        if (peer.Role == NodeRole.Mediator || _node.Role == NodeRole.Mediator)
        {
            priority = links.Where(link => link.Priority < EndpointPriority).Select(link => link.Priority + 10).DefaultIfEmpty(10).Max();
            remotePort = 22;
        }
        else if (peer.Role == counterpart)
        {
            priority = links.Select(link => link.Priority + 10).Append(EndpointPriority).Max();

            // A peer without an SSH server is reached on its TLS port instead of its loopback endpoint.
            remotePort = peer.EndpointMode == EndpointMode.Direct ? imported.EndpointPort : imported.ListenPort;
        }
        else
        {
            return new ImportResult(peer.Id, peer.Role, false, null, $"Stored peer {peer.Id}; a {_node.Role} node does not link to a {peer.Role} node directly.");
        }

        var name = UniqueName(peer.Id.Value, links);
        var created = new ChainLinkConfig(Guid.NewGuid(), name, TransportKind.Direct, peer, 0, remotePort, priority);
        await _links.UpsertAsync(created, cancellationToken);
        return new ImportResult(peer.Id, peer.Role, true, created.Id, $"Imported {peer.Role} {peer.Id} and added link '{name}'.");
    }

    /// <summary>
    /// Serializes an exported configuration to indented JSON.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(ExportedNodeConfiguration configuration) => JsonSerializer.Serialize(configuration, JsonOptions);

    /// <summary>
    /// Parses and validates an exported configuration.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="FormatException">The text is not a valid export.</exception>
    public static ExportedNodeConfiguration Parse(string json)
    {
        ExportedNodeConfiguration? configuration;
        try
        {
            configuration = JsonSerializer.Deserialize<ExportedNodeConfiguration>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The configuration file is not valid: {ex.Message}", ex);
        }

        Validate(configuration);
        return configuration!;
    }

    /// <summary>
    /// Validates an exported configuration.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <exception cref="FormatException">The configuration is invalid.</exception>
    private static void Validate(ExportedNodeConfiguration? configuration)
    {
        if (configuration is null)
        {
            throw new FormatException("The configuration file is empty.");
        }

        if (configuration.FormatVersion != ExportedNodeConfiguration.CurrentFormatVersion)
        {
            throw new FormatException($"Unsupported configuration format version {configuration.FormatVersion}.");
        }

        if (configuration.NodeId.IsEmpty || string.IsNullOrWhiteSpace(configuration.Name) || !Enum.IsDefined(configuration.Role))
        {
            throw new FormatException("The configuration must contain a node identifier, a name and a valid role.");
        }

        if (configuration.ListenPort is < 1 or > 65535)
        {
            throw new FormatException($"Listen port {configuration.ListenPort} is invalid.");
        }

        if (!Enum.IsDefined(configuration.EndpointMode))
        {
            throw new FormatException($"Endpoint mode '{configuration.EndpointMode}' is unknown.");
        }

        if (configuration.EndpointMode == EndpointMode.Direct)
        {
            if (configuration.EndpointPort is < 1 or > 65535)
            {
                throw new FormatException($"Endpoint port {configuration.EndpointPort} is invalid.");
            }

            if (NodeCertificateStore.NormalizeFingerprint(configuration.TlsFingerprint) is null)
            {
                throw new FormatException("A node that accepts connections on its own port must publish its TLS certificate fingerprint.");
            }
        }

        if (configuration.SigningPublicKey is not null)
        {
            try
            {
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(configuration.SigningPublicKey), out _);
                if (key.KeySize != 256)
                {
                    throw new FormatException("The signing public key must be an ECDSA P-256 key.");
                }
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException and not { Message: "The signing public key must be an ECDSA P-256 key." })
            {
                throw new FormatException("The signing public key is not a valid ECDSA public key.", ex);
            }
        }

        if (configuration.SshPublicKey is not null)
        {
            SshPublicKey.Parse(configuration.SshPublicKey);
        }

        if (!string.IsNullOrWhiteSpace(configuration.HostKeyFingerprint) && NormalizeHostKeyFingerprint(configuration.HostKeyFingerprint) is null)
        {
            throw new FormatException("The host key fingerprint must be an OpenSSH SHA256 fingerprint.");
        }
    }

    /// <summary>
    /// Normalizes an OpenSSH SHA-256 host key fingerprint.
    /// </summary>
    /// <param name="fingerprint">The fingerprint, with or without the <c>SHA256:</c> prefix.</param>
    /// <returns>The normalized fingerprint, or <see langword="null"/> when the value is blank or not a SHA-256 fingerprint.</returns>
    public static string? NormalizeHostKeyFingerprint(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        var normalized = SshPublicKey.NormalizeFingerprint(fingerprint);
        var hash = normalized["SHA256:".Length..];
        return hash.Length == 43 && hash.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/') ? normalized : null;
    }

    /// <summary>
    /// Returns a link name that is not used yet.
    /// </summary>
    /// <param name="baseName">The preferred name.</param>
    /// <param name="links">The existing links.</param>
    /// <returns>The unique name.</returns>
    private static string UniqueName(string baseName, IReadOnlyList<ChainLinkConfig> links)
    {
        var names = links.Select(link => link.Name).ToHashSet(StringComparer.Ordinal);
        var name = baseName;
        for (var i = 2; names.Contains(name); i++)
        {
            name = $"{baseName}-{i}";
        }

        return name;
    }
}
