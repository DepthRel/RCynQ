using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Diagnostics;
using RCynQ.Git.Commands;
using RCynQ.Localization;

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
            ? Fail(Texts.Format("Diag.Git.NotRunnable", _git.GitExecutable), Texts.Get("Diag.Git.InstallFix"))
            : Pass(output);
    }
}
