using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Git.Commands;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks that the Dest repository is a valid work tree with a committer identity.
/// </summary>
public sealed class RepoValidCheck : DiagnosticCheckBase
{
    /// <summary>The git runner.</summary>
    private readonly IGitCommandRunner _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepoValidCheck"/> class.
    /// </summary>
    /// <param name="git">The git runner.</param>
    public RepoValidCheck(IGitCommandRunner git)
        : base("RepoValid", new[] { NodeRole.Dest })
    {
        _git = git;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var repo = context.Node.RepoPath;
        if (repo is null || !Directory.Exists(repo))
        {
            return Fail(Texts.Format("Diag.Repo.Missing", repo), Texts.Get("Diag.Repo.MissingFix"));
        }

        try
        {
            var inside = await _git.RunAsync(new[] { "rev-parse", "--is-inside-work-tree" }, repo, cancellationToken: cancellationToken);
            if (inside.ExitCode != 0 || inside.StandardOutput.Trim() != "true")
            {
                return Fail(Texts.Format("Diag.Repo.NotWorkTree", repo), Texts.Get("Diag.Repo.NotWorkTreeFix"));
            }

            var name = await _git.RunAsync(new[] { "config", "user.name" }, repo, cancellationToken: cancellationToken);
            var email = await _git.RunAsync(new[] { "config", "user.email" }, repo, cancellationToken: cancellationToken);
            return name.ExitCode == 0 && email.ExitCode == 0 && name.StandardOutput.Trim().Length > 0 && email.StandardOutput.Trim().Length > 0
                ? Pass(Texts.Format("Diag.Repo.Ok", name.StandardOutput.Trim(), email.StandardOutput.Trim()))
                : Fail(Texts.Get("Diag.Repo.NoIdentity"), Texts.Get("Diag.Repo.NoIdentityFix"));
        }
        catch (SyncBridgeException ex)
        {
            return Fail(ex.Message, Texts.Get("Diag.Repo.InstallGit"));
        }
    }
}
