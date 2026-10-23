using System.Globalization;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Git.Commands;

namespace RCynQ.Git.Commits;

/// <summary>
/// Default <see cref="IGitCommitService"/>.
/// </summary>
/// <remarks>
/// Every commit name is checked before it reaches Git, so nothing from the network can be read as an option. New objects
/// of a received bundle stay in a quarantine (<see cref="GitQuarantine"/>) until they are checked; Git reads them there
/// through <c>GIT_OBJECT_DIRECTORY</c> with the repository as an alternate.
/// </remarks>
public sealed class GitCommitService : IGitCommitService
{
    /// <summary>
    /// The prefix of the reference that names the tip of an outgoing bundle for the time it is written.
    /// </summary>
    private const string OUTGOING_REF_PREFIX = "refs/rcynq/outgoing-";

    /// <summary>
    /// Separates the fields of a commit in the output of <c>git log</c>.
    /// </summary>
    private const char FIELD_SEPARATOR = '\u001f';

    /// <summary>
    /// Ends a commit in the output of <c>git log</c>.
    /// </summary>
    private const char RECORD_SEPARATOR = '\u001e';

    /// <summary>
    /// The Git command runner.
    /// </summary>
    private readonly IGitCommandRunner _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitCommitService"/> class.
    /// </summary>
    /// <param name="git">The Git command runner.</param>
    public GitCommitService(IGitCommandRunner git)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommitInfo>> ListFirstParentAsync(string directory, string fromCommit, string toCommit, int max, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        EnsureCommit(fromCommit, nameof(fromCommit));
        EnsureCommit(toCommit, nameof(toCommit));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max);

        var result = await _git.RunAsync(
            new[] { "log", "--first-parent", "-n", max.ToString(CultureInfo.InvariantCulture), "--format=%H%x1f%an <%ae>%x1f%aI%x1f%s%x1e", $"{fromCommit}..{toCommit}" },
            directory,
            cancellationToken: cancellationToken);
        EnsureSuccess(result, "git log");

        // git log lists the newest first; the list is read oldest first.
        return result.StandardOutput
            .Split(RECORD_SEPARATOR, StringSplitOptions.RemoveEmptyEntries)
            .Select(record => record.Trim('\r', '\n'))
            .Where(record => record.Length > 0)
            .Select(ParseCommit)
            .Reverse()
            .ToArray();
    }

    /// <inheritdoc />
    public async Task CreateBundleAsync(string directory, string bundlePath, string baseCommit, string tipCommit, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        EnsureFullPath(bundlePath, nameof(bundlePath));
        EnsureCommit(baseCommit, nameof(baseCommit));
        EnsureCommit(tipCommit, nameof(tipCommit));

        var reference = OUTGOING_REF_PREFIX + Guid.NewGuid().ToString("N");
        EnsureSuccess(await _git.RunAsync(new[] { "update-ref", "-m", "rcynq: commits to deliver", reference, tipCommit }, directory, cancellationToken: cancellationToken), "git update-ref");
        try
        {
            var bundle = await _git.RunAsync(new[] { "bundle", "create", "--quiet", bundlePath, $"{baseCommit}..{reference}" }, directory, cancellationToken: cancellationToken);
            EnsureSuccess(bundle, "git bundle create");
        }
        finally
        {
            // Not cancelled with the call: the reference must not outlive it.
            await _git.RunAsync(new[] { "update-ref", "--no-deref", "-d", reference }, directory, cancellationToken: CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async Task<GitQuarantine> UnbundleAsync(string directory, string bundlePath, string quarantineDirectory, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        EnsureFullPath(bundlePath, nameof(bundlePath));
        EnsureFullPath(quarantineDirectory, nameof(quarantineDirectory));

        var objects = await _git.RunAsync(new[] { "rev-parse", "--git-path", "objects" }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(objects, "git rev-parse --git-path");
        var repositoryObjects = Path.GetFullPath(Path.Combine(directory, objects.StandardOutput.Trim()));
        Directory.CreateDirectory(Path.Combine(quarantineDirectory, "pack"));
        var empty = new GitQuarantine(quarantineDirectory, repositoryObjects, Array.Empty<GitBranchTip>());

        // unbundle refuses a bundle whose prerequisites the repository lacks, and writes the pack into the quarantine only.
        var unbundle = await _git.RunAsync(new[] { "bundle", "unbundle", bundlePath }, directory, empty.Environment, cancellationToken);
        EnsureSuccess(unbundle, "git bundle unbundle");

        var heads = unbundle.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && GitNames.IsCommit(parts[0]))
            .Select(parts => new GitBranchTip(parts[1], parts[0]))
            .ToArray();
        return empty with { Heads = heads };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitCommitLink>> GetFirstParentChainAsync(string directory, string baseCommit, string tipCommit, GitQuarantine? quarantine, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        EnsureCommit(baseCommit, nameof(baseCommit));
        EnsureCommit(tipCommit, nameof(tipCommit));

        var environment = quarantine?.Environment;
        var ancestor = await _git.RunAsync(new[] { "merge-base", "--is-ancestor", baseCommit, tipCommit }, directory, environment, cancellationToken);
        if (ancestor.ExitCode != 0)
        {
            return Array.Empty<GitCommitLink>();
        }

        var chain = await _git.RunAsync(new[] { "rev-list", "--first-parent", "--reverse", "--parents", $"{baseCommit}..{tipCommit}" }, directory, environment, cancellationToken);
        EnsureSuccess(chain, "git rev-list");
        return chain.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(parts => new GitCommitLink(parts[0], parts.Length > 1 ? parts[1] : null))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> GetTouchedPathsAsync(string directory, string baseCommit, string tipCommit, GitQuarantine? quarantine, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        EnsureCommit(baseCommit, nameof(baseCommit));
        EnsureCommit(tipCommit, nameof(tipCommit));

        // -m lists the paths of a merge against each of its parents; with -z the paths are separated by NUL and never quoted.
        var result = await _git.RunAsync(
            new[] { "log", "-m", "--no-renames", "--name-only", "--format=", "-z", $"{baseCommit}..{tipCommit}" },
            directory,
            quarantine?.Environment,
            cancellationToken);
        EnsureSuccess(result, "git log");
        return result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim('\r', '\n'))
            .Where(path => path.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> MovePacks(GitQuarantine quarantine)
    {
        ArgumentNullException.ThrowIfNull(quarantine);
        var source = Path.Combine(quarantine.Directory, "pack");
        var target = Path.Combine(quarantine.RepositoryObjects, "pack");
        Directory.CreateDirectory(target);

        // The index makes a pack visible to Git, so it is moved after the pack and its reverse index.
        var files = Directory.Exists(source)
            ? Directory.GetFiles(source, "pack-*").OrderBy(file => Path.GetExtension(file) == ".idx" ? 1 : 0).ThenBy(file => file, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        var moved = new List<string>();
        try
        {
            foreach (var file in files)
            {
                var destination = Path.Combine(target, Path.GetFileName(file));

                // A pack of the same name holds the same objects; the repository already has it.
                if (File.Exists(destination))
                {
                    continue;
                }

                File.Move(file, destination);
                moved.Add(destination);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RemovePacks(moved);
            throw new IOException(ex.Message, ex);
        }

        return moved;
    }

    /// <inheritdoc />
    public void RemovePacks(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        // The index first: without it Git no longer sees the pack.
        foreach (var file in files.OrderBy(file => Path.GetExtension(file) == ".idx" ? 0 : 1))
        {
            try
            {
                // Git writes packs read-only, and Windows refuses to delete a read-only file.
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing refers to the objects of the pack; Git's own housekeeping removes them later.
            }
        }
    }

    /// <inheritdoc />
    public async Task<string> FastForwardAsync(string directory, string tipCommit, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        EnsureCommit(tipCommit, nameof(tipCommit));
        var result = await _git.RunInWorkingTreeAsync(new[] { "merge", "--ff-only", "--no-edit", tipCommit }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(result, "git merge --ff-only");
        return (result.StandardOutput + result.StandardError).Trim();
    }

    /// <inheritdoc />
    public async Task ResetAsync(string directory, string targetCommit, CommitResetMode mode, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        EnsureCommit(targetCommit, nameof(targetCommit));
        var option = Guard.Defined(mode, nameof(mode)) switch
        {
            CommitResetMode.Soft => "--soft",
            CommitResetMode.Mixed => "--mixed",
            _ => "--hard"
        };

        var result = await _git.RunInWorkingTreeAsync(new[] { "reset", "--quiet", option, targetCommit }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(result, "git reset");
    }

    /// <summary>
    /// Reads one commit of the output of <c>git log</c>.
    /// </summary>
    /// <param name="record">The fields: hash, author, author date, subject.</param>
    /// <returns>The commit.</returns>
    /// <exception cref="GitExecutionException">The record is not what the format asked for.</exception>
    private static CommitInfo ParseCommit(string record)
    {
        var fields = record.Split(FIELD_SEPARATOR);
        if (fields.Length != 4 ||
            !GitNames.IsCommit(fields[0]) ||
            !DateTimeOffset.TryParse(fields[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new GitExecutionException($"Unexpected git log record: '{record}'.");
        }

        return new CommitInfo(fields[0], Clean(fields[3], CommitInfo.MAX_SUBJECT_LENGTH), Clean(fields[1], CommitInfo.MAX_AUTHOR_LENGTH), date);
    }

    /// <summary>
    /// Makes a text of a commit fit to be shown and signed: control characters become spaces, the text is cut to a length.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="maxLength">The longest text.</param>
    /// <returns>The text.</returns>
    private static string Clean(string text, int maxLength)
    {
        var clean = new string(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    /// <summary>
    /// Throws when a value is not a full commit name, so it cannot reach Git as an option or a range.
    /// </summary>
    /// <param name="commit">The value.</param>
    /// <param name="parameterName">The parameter name used in the exception.</param>
    /// <exception cref="ArgumentException">The value is not a full commit name.</exception>
    private static void EnsureCommit(string commit, string parameterName)
    {
        if (!GitNames.IsCommit(commit))
        {
            throw new ArgumentException($"'{commit}' is not a full commit name.", parameterName);
        }
    }

    /// <summary>
    /// Throws when a value is not a full path, so it cannot reach Git as an option.
    /// </summary>
    /// <param name="path">The value.</param>
    /// <param name="parameterName">The parameter name used in the exception.</param>
    /// <exception cref="ArgumentException">The value is not a full path.</exception>
    private static void EnsureFullPath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Any(char.IsControl))
        {
            throw new ArgumentException($"'{path}' is not a full path.", parameterName);
        }
    }

    /// <summary>
    /// Throws when a git command failed.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="command">The command description.</param>
    /// <exception cref="GitExecutionException">The exit code is not zero.</exception>
    private static void EnsureSuccess(ProcessResult result, string command)
    {
        if (result.ExitCode != 0)
        {
            throw new GitExecutionException($"{command} failed with exit code {result.ExitCode}: {result.StandardError.Trim()}", result.ExitCode);
        }
    }
}
