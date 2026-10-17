using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Config;

/// <summary>
/// <c>rcynq export</c>: writes the public node configuration for the neighbours.
/// </summary>
[Command("export", Description = "Exports the public node configuration (no private keys).")]
public sealed class ExportCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExportCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public ExportCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the output file.</summary>
    [CommandOption("output", 'o', Description = "Output file; printed to stdout when omitted.")]
    public string? Output { get; set; }

    /// <summary>Gets or sets the advertised addresses, the preferred one first.</summary>
    [CommandOption("host", Description = "Addresses at which neighbours reach this node, the preferred one first (repeat or list several).")]
    public IReadOnlyList<string>? Hosts { get; set; }

    /// <summary>Gets or sets the advertised SSH user.</summary>
    [CommandOption("user", Description = "SSH user neighbours log in with.")]
    public string? User { get; set; }

    /// <summary>Gets or sets the advertised SSH port.</summary>
    [CommandOption("ssh-port", Description = "SSH port (default: 22, or the local sshd port with --auto).")]
    public int? SshPort { get; set; }

    /// <summary>Gets or sets the SSH public key file.</summary>
    [CommandOption("ssh-public-key-file", Description = "This node's SSH public key (.pub) for restricted authorized_keys entries.")]
    public string? SshPublicKeyFile { get; set; }

    /// <summary>Gets or sets the sshd host key fingerprint.</summary>
    [CommandOption("host-key-fingerprint", Description = "SHA256 fingerprint of this node's sshd host key, pinned by the neighbour.")]
    public string? HostKeyFingerprint { get; set; }

    /// <summary>Gets or sets a value indicating whether missing values are detected automatically.</summary>
    [CommandOption("auto", Description = "Detect the address, SSH user, public key and host key fingerprint; explicit options win.")]
    public bool Auto { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var service = context.Get<ExportImportService>();
        var hosts = Hosts is { Count: > 0 } ? Hosts : null;
        var user = User;
        var port = SshPort;
        var sshKey = SshPublicKeyFile is null ? null : (await File.ReadAllTextAsync(SshPublicKeyFile, cancellationToken)).Trim();
        var fingerprint = HostKeyFingerprint;

        if (Auto)
        {
            var report = await service.ProbeAsync(cancellationToken);
            hosts ??= report.Addresses.Select(address => address.Address).ToArray();
            user ??= string.IsNullOrEmpty(report.SshUser) ? null : report.SshUser;
            port ??= report.SshPort;
            sshKey ??= report.SshPublicKey;
            fingerprint ??= report.HostKeyFingerprints.FirstOrDefault();
            foreach (var warning in report.Warnings)
            {
                await console.Error.WriteLineAsync($"warning: {warning}");
            }

            await console.Error.WriteLineAsync($"Detected host {(hosts.Count == 0 ? "-" : string.Join(", ", hosts))}, user {user ?? "-"}, SSH port {port}, key {report.SshKeyFingerprint ?? "-"}.");
        }

        var json = await service.ExportAsync(hosts ?? Array.Empty<string>(), user, port ?? 22, sshKey, fingerprint, cancellationToken);
        if (Output is null)
        {
            await console.Output.WriteLineAsync(json);
            return;
        }

        await File.WriteAllTextAsync(Output, json, cancellationToken);
        await console.Output.WriteLineAsync($"Exported to {Path.GetFullPath(Output)}.");
    }
}
