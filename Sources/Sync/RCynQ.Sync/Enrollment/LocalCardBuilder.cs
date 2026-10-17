using RCynQ.Core.Api;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Configuration.Probing;

namespace RCynQ.Sync.Enrollment;

/// <summary>
/// Builds the card of this node without asking anything, the way the export dialog fills itself: every address the
/// computer has, its SSH user and port, its SSH key (created when it has none) and the host key of its SSH server.
/// </summary>
public sealed class LocalCardBuilder
{
    /// <summary>The probe of the computer.</summary>
    private readonly INodeEnvironmentProbe _probe;

    /// <summary>The export of the card.</summary>
    private readonly NodeConfigurationTransfer _transfer;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalCardBuilder"/> class.
    /// </summary>
    /// <param name="probe">The probe of the computer.</param>
    /// <param name="transfer">The export of the card.</param>
    public LocalCardBuilder(INodeEnvironmentProbe probe, NodeConfigurationTransfer transfer)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
    }

    /// <summary>
    /// Builds the card.
    /// </summary>
    /// <param name="configPath">The configuration file, which gets the path of a key that is created.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The card.</returns>
    /// <exception cref="ArgumentException">A detected value cannot go into a card.</exception>
    public async Task<ExportedNodeConfiguration> BuildAsync(string configPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        var report = await _probe.ProbeAsync(cancellationToken);
        if (report.SshPublicKey is null)
        {
            report = await _probe.GenerateSshKeyAsync(configPath, cancellationToken);
        }

        var addresses = report.Addresses.Select(address => address.Address).Take(ExportedNodeConfiguration.MAX_ADDRESSES).ToArray();
        var user = string.IsNullOrEmpty(report.SshUser) ? null : report.SshUser;
        return await _transfer.ExportAsync(addresses, user, report.SshPort, report.SshPublicKey, report.HostKeyFingerprints.FirstOrDefault(), cancellationToken);
    }
}
