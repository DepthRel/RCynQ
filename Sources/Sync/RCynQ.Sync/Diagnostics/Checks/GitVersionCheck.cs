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
/// Checks that git is version 2.30 or newer.
/// </summary>
public sealed partial class GitVersionCheck : DiagnosticCheckBase
{
    /// <summary>The minimum supported version.</summary>
    public static readonly Version MinimumVersion = new(2, 30);

    /// <summary>The process runner.</summary>
    private readonly IProcessRunner _processes;

    /// <summary>The git options.</summary>
    private readonly GitOptions _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitVersionCheck"/> class.
    /// </summary>
    /// <param name="processes">The process runner.</param>
    /// <param name="git">The git options.</param>
    public GitVersionCheck(IProcessRunner processes, GitOptions git)
        : base("GitVersion", PatchRoles)
    {
        _processes = processes;
        _git = git;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var output = await TryGetVersionOutputAsync(_processes, _git, cancellationToken);
        if (output is null)
        {
            return Fail("Git is not available, so its version cannot be checked.", "Install Git 2.30 or newer.");
        }

        var version = ParseVersion(output);
        if (version is null)
        {
            return Fail($"Unrecognized git version output '{output}'.");
        }

        return version >= MinimumVersion
            ? Pass($"Git {version}.")
            : Fail($"Git {version} is older than {MinimumVersion}.", "Upgrade Git to 2.30 or newer (required for reliable apply --3way and no-index diffs).");
    }

    /// <summary>
    /// Parses <c>git version X.Y.Z...</c>.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns>The version, or <see langword="null"/>.</returns>
    public static Version? ParseVersion(string output)
    {
        var match = VersionPattern().Match(output);
        return match.Success
            ? new Version(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))
            : null;
    }

    /// <summary>
    /// Runs <c>git --version</c>.
    /// </summary>
    /// <param name="processes">The process runner.</param>
    /// <param name="git">The git options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The trimmed output, or <see langword="null"/> when git cannot be executed.</returns>
    internal static async Task<string?> TryGetVersionOutputAsync(IProcessRunner processes, GitOptions git, CancellationToken cancellationToken)
    {
        try
        {
            var result = await processes.RunAsync(new ProcessSpec(git.GitExecutable, new[] { "--version" }, null, null, TimeSpan.FromSeconds(30)), cancellationToken);
            return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
        }
        catch (SyncBridgeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Pattern for the git version output.
    /// </summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex(@"git version (\d+)\.(\d+)")]
    private static partial Regex VersionPattern();
}
