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

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SshClientCheck"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="processes">The process runner.</param>
    /// <param name="ssh">The SSH options.</param>
    public SshClientCheck(ILocalizer localizer, IProcessRunner processes, SshClientOptions ssh)
        : base("SshClient", _allRoles)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
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
                : Fail(_localizer.Format("Diag.SshClient.Failed", _ssh.SshExecutable, version), _localizer.Get("Diag.SshClient.Install"));
        }
        catch (SyncBridgeException ex)
        {
            return Fail(_localizer.Describe(ex), _localizer.Get("Diag.SshClient.InstallFix"));
        }
    }
}
