using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Ssh.Keys;
using RCynQ.Sync.Security;
using RCynQ.Transports.Abstractions;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that git can be executed.
/// </summary>
public sealed class GitInstalledCheck : DiagnosticCheckBase
{
    /// <summary>The process runner.</summary>
    private readonly IProcessRunner _processes;

    /// <summary>The git options.</summary>
    private readonly GitOptions _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitInstalledCheck"/> class.
    /// </summary>
    /// <param name="processes">The process runner.</param>
    /// <param name="git">The git options.</param>
    public GitInstalledCheck(IProcessRunner processes, GitOptions git)
        : base("GitInstalled", PatchRoles)
    {
        _processes = processes;
        _git = git;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var output = await GitVersionCheck.TryGetVersionOutputAsync(_processes, _git, cancellationToken);
        return output is null
            ? Fail($"'{_git.GitExecutable}' could not be executed.", "Install Git 2.30 or newer and make sure it is on PATH (or set SyncBridge:Ssh:GitExecutable).")
            : Pass(output);
    }
}
