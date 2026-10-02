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

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitInstalledCheck"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="processes">The process runner.</param>
    /// <param name="git">The git options.</param>
    public GitInstalledCheck(ILocalizer localizer, IProcessRunner processes, GitOptions git)
        : base("GitInstalled", PatchRoles)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _processes = processes;
        _git = git;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var output = await GitVersionCheck.TryGetVersionOutputAsync(_processes, _git, cancellationToken);
        return output is null
            ? Fail(_localizer.Format("Diag.Git.NotRunnable", _git.GitExecutable), _localizer.Get("Diag.Git.InstallFix"))
            : Pass(output);
    }
}
