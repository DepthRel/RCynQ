using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Git.Commands;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the Origin repository is a valid work tree with a committer identity.
/// </summary>
public sealed class RepoValidCheck : DiagnosticCheckBase
{
    /// <summary>The git runner.</summary>
    private readonly IGitCommandRunner _git;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepoValidCheck"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="git">The git runner.</param>
    public RepoValidCheck(ILocalizer localizer, IGitCommandRunner git)
        : base("RepoValid", new[] { NodeRole.Origin })
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _git = git;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var repo = context.Node.RepoPath;
        if (repo is null || !Directory.Exists(repo))
        {
            return Fail(_localizer.Format("Diag.Repo.Missing", repo), _localizer.Get("Diag.Repo.MissingFix"));
        }

        try
        {
            var inside = await _git.RunAsync(new[] { "rev-parse", "--is-inside-work-tree" }, repo, cancellationToken: cancellationToken);
            if (inside.ExitCode != 0 || inside.StandardOutput.Trim() != "true")
            {
                return Fail(_localizer.Format("Diag.Repo.NotWorkTree", repo), _localizer.Get("Diag.Repo.NotWorkTreeFix"));
            }

            var name = await _git.RunAsync(new[] { "config", "user.name" }, repo, cancellationToken: cancellationToken);
            var email = await _git.RunAsync(new[] { "config", "user.email" }, repo, cancellationToken: cancellationToken);
            return name.ExitCode == 0 && email.ExitCode == 0 && name.StandardOutput.Trim().Length > 0 && email.StandardOutput.Trim().Length > 0
                ? Pass(_localizer.Format("Diag.Repo.Ok", name.StandardOutput.Trim(), email.StandardOutput.Trim()))
                : Fail(_localizer.Get("Diag.Repo.NoIdentity"), _localizer.Get("Diag.Repo.NoIdentityFix"));
        }
        catch (SyncBridgeException ex)
        {
            return Fail(_localizer.Describe(ex), _localizer.Get("Diag.Repo.InstallGit"));
        }
    }
}
