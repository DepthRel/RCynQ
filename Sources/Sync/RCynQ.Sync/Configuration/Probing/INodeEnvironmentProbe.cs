using RCynQ.Core.Api;

namespace RCynQ.Sync.Configuration.Probing;

/// <summary>
/// Collects the network and SSH environment of the local node for an export.
/// </summary>
public interface INodeEnvironmentProbe
{
    /// <summary>
    /// Probes the local addresses, the SSH server, the service port and the SSH keys.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report.</returns>
    Task<NodeEnvironmentReport> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates the node SSH key when none is configured, records it as <c>Ssh.IdentityFile</c> and probes again.
    /// </summary>
    /// <param name="configPath">The configuration file that receives the identity file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report after the key exists.</returns>
    Task<NodeEnvironmentReport> GenerateSshKeyAsync(string configPath, CancellationToken cancellationToken = default);
}
