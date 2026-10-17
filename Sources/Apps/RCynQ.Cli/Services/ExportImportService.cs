using RCynQ.Core.Api;
using RCynQ.Core.Enums;
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
    /// <param name="addresses">The advertised addresses, the preferred one first.</param>
    /// <param name="user">The advertised SSH user.</param>
    /// <param name="sshPort">The advertised SSH port.</param>
    /// <param name="sshPublicKey">This node's SSH public key line, or <see langword="null"/>.</param>
    /// <param name="hostKeyFingerprint">The sshd host key fingerprint, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The JSON text.</returns>
    /// <exception cref="ArgumentException">A value is invalid.</exception>
    public async Task<string> ExportAsync(IReadOnlyList<string> addresses, string? user, int sshPort, string? sshPublicKey, string? hostKeyFingerprint, CancellationToken cancellationToken = default)
        => NodeConfigurationTransfer.Serialize(await _transfer.ExportAsync(addresses, user, sshPort, sshPublicKey, hostKeyFingerprint, cancellationToken));

    /// <summary>
    /// Imports a peer configuration file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A summary.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="FormatException">The file is not a valid export, or the import needs a decision.</exception>
    public Task<string> ImportAsync(string path, CancellationToken cancellationToken = default)
        => ImportAsync(path, null, null, cancellationToken);

    /// <summary>
    /// Imports a peer configuration file, with the decision for a node of a role that is known already.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="decision">The decision, or <see langword="null"/> when none is given.</param>
    /// <param name="linkId">The link whose peer the imported node replaces, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A summary.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="FormatException">
    /// The file is not a valid export, or the import needs a decision that was not given; nothing is imported then, and
    /// the message names the decisions to choose from.
    /// </exception>
    public async Task<string> ImportAsync(string path, ImportDecision? decision, Guid? linkId, CancellationToken cancellationToken = default)
    {
        var configuration = NodeConfigurationTransfer.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        var result = await _transfer.ImportAsync(configuration, decision, linkId, cancellationToken);
        if (result.Decision is { } needed)
        {
            // A command cannot ask: it refuses and tells how to say what the file is.
            var lines = new List<string> { result.Message, "Repeat the import with one of:" };
            lines.AddRange(needed.Options.Select(option => $"  --as {OptionName(option)}"));
            if (!needed.SameNode && needed.Options.Contains(ImportDecision.Replace) && needed.Candidates.Count > 1)
            {
                lines.Add("With '--as replace' name the link to change with --link:");
                lines.AddRange(needed.Candidates.Select(candidate => $"  {candidate.LinkName}  ({candidate.PeerId.Value}, {candidate.Host})"));
            }

            throw new FormatException(string.Join("\n", lines));
        }

        return result.Message + "\n";
    }

    /// <summary>
    /// Reads the value of the <c>--as</c> option.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/> when the option is not given.</param>
    /// <returns>The decision, or <see langword="null"/>.</returns>
    /// <exception cref="FormatException">The value names no decision.</exception>
    public static ImportDecision? ParseDecision(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "route" or "alternative" => ImportDecision.AlternativeRoute,
            "replace" => ImportDecision.Replace,
            "hop" or "add" => ImportDecision.AddHop,
            _ => throw new FormatException($"Unknown value '{value}' of --as; use 'route', 'replace' or 'hop'.")
        };

    /// <summary>
    /// Gets the value of the <c>--as</c> option that names a decision.
    /// </summary>
    /// <param name="decision">The decision.</param>
    /// <returns>The value.</returns>
    private static string OptionName(ImportDecision decision) => decision switch
    {
        ImportDecision.AlternativeRoute => "route    (another address of the same node)",
        ImportDecision.Replace => "replace  (takes the place of a known node or address)",
        _ => "hop      (one more node next to the known ones)"
    };
}
