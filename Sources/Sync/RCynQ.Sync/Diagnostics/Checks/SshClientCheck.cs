using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the OpenSSH client can be executed.
/// </summary>
public sealed class SshClientCheck : DiagnosticCheckBase
{
    /// <summary>The process runner.</summary>
    private readonly IProcessRunner _processes;

    /// <summary>The SSH options.</summary>
    private readonly SshClientOptions _ssh;

    /// <summary>
    /// Initializes a new instance of the <see cref="SshClientCheck"/> class.
    /// </summary>
    /// <param name="processes">The process runner.</param>
    /// <param name="ssh">The SSH options.</param>
    public SshClientCheck(IProcessRunner processes, SshClientOptions ssh)
        : base("SshClient", AllRoles)
    {
        _processes = processes;
        _ssh = ssh;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processes.RunAsync(new ProcessSpec(_ssh.SshExecutable, new[] { "-V" }, null, null, TimeSpan.FromSeconds(30)), cancellationToken);
            var version = (result.StandardError + result.StandardOutput).Trim();
            return result.ExitCode == 0
                ? Pass(version)
                : Fail(Texts.Format("Diag.SshClient.Failed", _ssh.SshExecutable, version), Texts.Get("Diag.SshClient.Install"));
        }
        catch (SyncBridgeException ex)
        {
            return Fail(ex.Message, Texts.Get("Diag.SshClient.InstallFix"));
        }
    }
}
