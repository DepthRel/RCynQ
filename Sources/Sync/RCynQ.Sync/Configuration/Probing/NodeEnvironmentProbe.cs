using System.Text.Json.Nodes;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.HostKeys;
using RCynQ.Sync.Endpoint;

namespace RCynQ.Sync.Configuration.Probing;

/// <summary>
/// <see cref="INodeEnvironmentProbe"/> using the interface table, TCP connects, <c>ssh-keyscan</c> and <c>ssh-keygen</c>.
/// </summary>
public sealed class NodeEnvironmentProbe : INodeEnvironmentProbe
{
    /// <summary>
    /// The connect timeout of a single reachability check.
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>The interface source.</summary>
    private readonly INetworkInterfaceSource _interfaces;

    /// <summary>The TCP probe.</summary>
    private readonly ITcpProbe _tcp;

    /// <summary>The host key reader.</summary>
    private readonly HostKeyVerifier _hostKeys;

    /// <summary>The SSH key generator.</summary>
    private readonly SshKeyGenerator _keys;

    /// <summary>The node data directories.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>The time provider.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeEnvironmentProbe"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="options">The node options.</param>
    /// <param name="interfaces">The interface source.</param>
    /// <param name="tcp">The TCP probe.</param>
    /// <param name="hostKeys">The host key reader.</param>
    /// <param name="keys">The SSH key generator.</param>
    /// <param name="appData">The node data directories; the SSH key is kept in <c>keys/ssh</c>.</param>
    /// <param name="timeProvider">The time provider.</param>
    public NodeEnvironmentProbe(
        ILocalizer localizer,
        NodeConfig node,
        SyncBridgeOptions options,
        INetworkInterfaceSource interfaces,
        ITcpProbe tcp,
        HostKeyVerifier hostKeys,
        SshKeyGenerator keys,
        IAppDataFactory appData,
        TimeProvider timeProvider)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _interfaces = interfaces ?? throw new ArgumentNullException(nameof(interfaces));
        _tcp = tcp ?? throw new ArgumentNullException(nameof(tcp));
        _hostKeys = hostKeys ?? throw new ArgumentNullException(nameof(hostKeys));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<NodeEnvironmentReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var sshPort = _options.Ssh.LocalSshdPort;

        // A node without an SSH server is reached on its own TLS port, so that is the port worth checking.
        var direct = _options.Endpoint.Mode == EndpointMode.Direct;
        var reachablePort = direct ? _options.Endpoint.Port : sshPort;
        var candidates = _interfaces.GetAddresses()
            .Select(address => (address, kind: NetworkAddressClassifier.Classify(address.Address, address.IsTunnelInterface)))
            .Where(item => item.kind is not (NetworkAddressKind.Loopback or NetworkAddressKind.LinkLocal))
            .DistinctBy(item => item.address.Address)
            .ToArray();

        var probed = await Task.WhenAll(candidates.Select(async item => new NetworkAddressInfo(
            item.address.Address,
            item.address.InterfaceName,
            item.kind,
            await _tcp.CanConnectAsync(item.address.Address, reachablePort, ConnectTimeout, cancellationToken))));
        var addresses = probed.OrderBy(NetworkAddressClassifier.Rank).ThenBy(info => info.Address, StringComparer.Ordinal).ToArray();

        var loopbackSsh = direct ? Task.FromResult(false) : _tcp.CanConnectAsync("127.0.0.1", sshPort, ConnectTimeout, cancellationToken);
        var servicePort = _tcp.CanConnectAsync("127.0.0.1", _options.ListenPort, ConnectTimeout, cancellationToken);
        var sshdListening = !direct && (await loopbackSsh || addresses.Any(info => info.SshReachable));
        var servicePortListening = await servicePort;

        var (publicKey, publicKeyPath) = await FindPublicKeyAsync(cancellationToken);
        var hostKeys = sshdListening ? await ReadHostKeysAsync(sshPort, cancellationToken) : Array.Empty<string>();

        var warnings = new List<EnvironmentWarning>();
        if (direct)
        {
            if (!addresses.Any(info => info.SshReachable))
            {
                warnings.Add(EnvironmentWarning.EndpointNotListening);
            }
        }
        else
        {
            if (!sshdListening)
            {
                warnings.Add(EnvironmentWarning.SshdNotListening);
            }

            if (!addresses.Any(info => info.SshReachable))
            {
                warnings.Add(EnvironmentWarning.NoReachableAddress);
            }
        }

        if (publicKey is null)
        {
            warnings.Add(EnvironmentWarning.NoSshPublicKey);
        }

        if (!servicePortListening)
        {
            warnings.Add(EnvironmentWarning.ServicePortNotListening);
        }

        if (sshdListening && hostKeys.Count == 0)
        {
            warnings.Add(EnvironmentWarning.HostKeysUnavailable);
        }

        var user = SshEndpoint.IsValidUser(Environment.UserName) ? Environment.UserName : string.Empty;
        return new NodeEnvironmentReport(
            Environment.MachineName,
            user,
            sshPort,
            sshdListening,
            servicePortListening,
            addresses,
            addresses.FirstOrDefault()?.Address,
            publicKey?.ToLine(),
            publicKeyPath,
            publicKey?.Fingerprint,
            hostKeys,
            warnings,
            _timeProvider.GetUtcNow(),
            _options.Endpoint.Mode,
            direct ? _options.Endpoint.Port : 0,
            direct ? ReadTlsFingerprint() : null);
    }

    /// <summary>
    /// Reads the fingerprint of the node's TLS certificate, creating the certificate when it is missing.
    /// </summary>
    /// <returns>The fingerprint.</returns>
    private string ReadTlsFingerprint()
    {
        using var certificate = NodeCertificateStore.GetOrCreate(_appData.Create(), _options.NodeId);
        return NodeCertificateStore.Fingerprint(certificate);
    }

    /// <inheritdoc />
    public async Task<NodeEnvironmentReport> GenerateSshKeyAsync(string configPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var configured = !string.IsNullOrWhiteSpace(_options.Ssh.IdentityFile);
        var keysDirectory = _appData.Create().SshKeysDirectory;
        var privateKey = configured ? _options.Ssh.IdentityFile!.Trim() : SshKeyLocator.DefaultPrivateKeyPath(keysDirectory);

        if (!File.Exists(privateKey))
        {
            if (!configured)
            {
                // Created before ssh-keygen runs, so the key files inherit owner-only access (OpenSSH refuses readable keys).
                Directory.CreateDirectory(keysDirectory);
                FilePermissions.RestrictDirectoryToOwner(keysDirectory);
            }

            await _keys.GenerateAsync(privateKey, $"rcynq:{_node.Id.Value}", cancellationToken);
            FilePermissions.RestrictFileToOwner(privateKey);
        }
        else if (!File.Exists(privateKey + ".pub"))
        {
            throw new SyncBridgeException(_localizer.Format("Probe.PublicKeyMissing", privateKey));
        }

        if (!configured)
        {
            await ConfigurationFileEditor.UpdateAsync(configPath, section => SetIdentityFile(section, privateKey), cancellationToken);
            _options.Ssh.IdentityFile = privateKey;
        }

        return await ProbeAsync(cancellationToken);
    }

    /// <summary>
    /// Writes <c>Ssh.IdentityFile</c>, reusing an existing <c>Ssh</c> object regardless of its key casing.
    /// </summary>
    /// <param name="section">The <c>SyncBridge</c> section.</param>
    /// <param name="privateKey">The private key path.</param>
    private static void SetIdentityFile(JsonObject section, string privateKey)
    {
        var sshKey = section.Select(pair => pair.Key).FirstOrDefault(key => key.Equals(nameof(SyncBridgeOptions.Ssh), StringComparison.OrdinalIgnoreCase));
        if (sshKey is null || section[sshKey] is not JsonObject ssh)
        {
            ssh = new JsonObject();
            section[sshKey ?? nameof(SyncBridgeOptions.Ssh)] = ssh;
        }

        var identityKey = ssh.Select(pair => pair.Key).FirstOrDefault(key => key.Equals(nameof(SshClientOptions.IdentityFile), StringComparison.OrdinalIgnoreCase))
            ?? nameof(SshClientOptions.IdentityFile);
        ssh[identityKey] = privateKey;
    }

    /// <summary>
    /// Finds the first readable SSH public key.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The key and its path, or <see langword="null"/> values.</returns>
    private async Task<(SshPublicKey? Key, string? Path)> FindPublicKeyAsync(CancellationToken cancellationToken)
    {
        foreach (var path in SshKeyLocator.CandidatePublicKeyPaths(_options.Ssh, _appData.Create().SshKeysDirectory))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                return (SshPublicKey.Parse(await File.ReadAllTextAsync(path, cancellationToken)), path);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                // An unreadable or unsupported key file is skipped; the next candidate is tried.
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Reads the host key fingerprints of the local SSH server.
    /// </summary>
    /// <param name="sshPort">The SSH port.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The fingerprints; empty when they cannot be read.</returns>
    private async Task<IReadOnlyList<string>> ReadHostKeysAsync(int sshPort, CancellationToken cancellationToken)
    {
        try
        {
            return await _hostKeys.GetHostFingerprintsAsync(new SshEndpoint("rcynq", "127.0.0.1", sshPort), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ssh-keyscan may be missing or fail; the report carries HostKeysUnavailable instead of an error.
            return Array.Empty<string>();
        }
    }
}
