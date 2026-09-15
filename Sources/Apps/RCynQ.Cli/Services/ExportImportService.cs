using RCynQ.Core.Api;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Configuration.Probing;

namespace RCynQ.Cli.Services;

/// <summary>
/// Exports the public node configuration and imports peer configurations. Private keys are never exported.
/// </summary>
public sealed class ExportImportService
{
    /// <summary>The configuration transfer.</summary>
    private readonly NodeConfigurationTransfer _transfer;

    /// <summary>The environment probe.</summary>
    private readonly INodeEnvironmentProbe _probe;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportImportService"/> class.
    /// </summary>
    /// <param name="transfer">The configuration transfer.</param>
    /// <param name="probe">The environment probe.</param>
    public ExportImportService(NodeConfigurationTransfer transfer, INodeEnvironmentProbe probe)
    {
        _transfer = transfer;
        _probe = probe;
    }

    /// <summary>
    /// Probes the network and SSH environment of the node.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report.</returns>
    public Task<NodeEnvironmentReport> ProbeAsync(CancellationToken cancellationToken = default) => _probe.ProbeAsync(cancellationToken);

    /// <summary>
    /// Exports the configuration as JSON.
    /// </summary>
    /// <param name="host">The advertised host.</param>
    /// <param name="user">The advertised SSH user.</param>
    /// <param name="sshPort">The advertised SSH port.</param>
    /// <param name="sshPublicKey">This node's SSH public key line, or <see langword="null"/>.</param>
    /// <param name="hostKeyFingerprint">The sshd host key fingerprint, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The JSON text.</returns>
    /// <exception cref="ArgumentException">A value is invalid.</exception>
    public async Task<string> ExportAsync(string? host, string? user, int sshPort, string? sshPublicKey, string? hostKeyFingerprint, CancellationToken cancellationToken = default)
        => NodeConfigurationTransfer.Serialize(await _transfer.ExportAsync(host, user, sshPort, sshPublicKey, hostKeyFingerprint, cancellationToken));

    /// <summary>
    /// Imports a peer configuration file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A summary.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="FormatException">The file is not a valid export.</exception>
    public async Task<string> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        var configuration = NodeConfigurationTransfer.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        var result = await _transfer.ImportAsync(configuration, cancellationToken);
        return result.Message + "\n";
    }
}
