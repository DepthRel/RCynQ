using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions;
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
            return Fail($"Repository path '{repo}' does not exist.", "Set SyncBridge:RepoPath to the closed repository.");
        }

        try
        {
            var inside = await _git.RunAsync(new[] { "rev-parse", "--is-inside-work-tree" }, repo, cancellationToken: cancellationToken);
            if (inside.ExitCode != 0 || inside.StandardOutput.Trim() != "true")
            {
                return Fail($"'{repo}' is not a Git work tree.", "Point SyncBridge:RepoPath at a non-bare clone of the repository.");
            }

            var name = await _git.RunAsync(new[] { "config", "user.name" }, repo, cancellationToken: cancellationToken);
            var email = await _git.RunAsync(new[] { "config", "user.email" }, repo, cancellationToken: cancellationToken);
            return name.ExitCode == 0 && email.ExitCode == 0 && name.StandardOutput.Trim().Length > 0 && email.StandardOutput.Trim().Length > 0
                ? Pass($"Repository is valid; committer {name.StandardOutput.Trim()} <{email.StandardOutput.Trim()}>.")
                : Fail("user.name or user.email is not configured.", "Run 'git config user.name ...' and 'git config user.email ...' in the repository.");
        }
        catch (SyncBridgeException ex)
        {
            return Fail(ex.Message, "Install Git.");
        }
    }
}
