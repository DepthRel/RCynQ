using System.Text;
using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Core.Validation;
using RCynQ.Git.Commands;

namespace RCynQ.Git.Patches.Files;

/// <summary>
/// Default <see cref="IGitPatchFileService"/>.
/// </summary>
/// <remarks>
/// <para>
/// A patch is built in a private index with a private object directory: <c>git add</c> runs the clean filters and line
/// ending rules of the repository, so the patch shows what <c>git diff</c> shows the user, also for new files, and the
/// repository, which on an Origin is only read, gets no object and no index update.
/// </para>
/// <para>
/// A working tree may be a subfolder of its repository. The paths of a patch are relative to the working tree, and
/// <c>git apply</c> gets the subfolder with <c>--directory</c>: without it the command skips every file outside the current
/// folder and reports success.
/// </para>
/// </remarks>
public sealed partial class GitPatchFileService : IGitPatchFileService
{
    /// <summary>The most characters of file patterns on a command line; Windows allows about 32 000 for all of it.</summary>
    private const int MAX_PATTERN_CHARACTERS = 30000;

    /// <summary>The environment that keeps the errors of Git in English, the only language <see cref="GitApplyErrorReader"/> reads.</summary>
    private static readonly IReadOnlyDictionary<string, string> _englishMessages = new Dictionary<string, string>
    {
        ["LC_ALL"] = "C",
        ["LANGUAGE"] = "C"
    };

    /// <summary>The git command runner.</summary>
    private readonly IGitCommandRunner _git;

    /// <summary>The node data directories, or <see langword="null"/> to work in the system temporary directory.</summary>
    private readonly IAppDataFactory? _appData;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitPatchFileService"/> class.
    /// </summary>
    /// <param name="git">The git command runner.</param>
    /// <param name="appData">The node data directories; temporary files go to their <c>tmp</c> directory.</param>
    public GitPatchFileService(IGitCommandRunner git, IAppDataFactory? appData = null)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _appData = appData;
    }

    /// <inheritdoc />
    public async Task<byte[]> BuildAsync(string directory, string commit, IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ArgumentNullException.ThrowIfNull(paths);
        if (!GitNames.IsCommit(commit))
        {
            throw new ArgumentException("The commit must be a full object name.", nameof(commit));
        }

        if (paths.Count == 0)
        {
            return Array.Empty<byte>();
        }

        var staging = CreateStaging();
        try
        {
            var objects = Path.Combine(staging, "objects");
            Directory.CreateDirectory(objects);
            var environment = new Dictionary<string, string>
            {
                ["GIT_INDEX_FILE"] = Path.Combine(staging, "index"),
                ["GIT_OBJECT_DIRECTORY"] = objects,
                ["GIT_ALTERNATE_OBJECT_DIRECTORIES"] = await GetObjectDirectoryAsync(directory, cancellationToken)
            };

            var read = await _git.RunInWorkingTreeAsync(new[] { "read-tree", commit }, directory, environment, cancellationToken);
            EnsureSuccess(read.ExitCode, read.StandardError, "git read-tree");

            var list = await WritePathspecAsync(staging, paths, cancellationToken);
            var add = await _git.RunInWorkingTreeAsync(new[] { "add", "--all", "--pathspec-from-file=" + list, "--pathspec-file-nul" }, directory, environment, cancellationToken);
            EnsureSuccess(add.ExitCode, add.StandardError, "git add");

            var output = Path.Combine(staging, "changes.patch");
            var diff = await _git.RunInWorkingTreeAsync(
                new[]
                {
                    "diff", "--cached", "--relative", "--binary", "--no-renames", "--no-color", "--no-ext-diff", "--no-textconv",
                    "--src-prefix=a/", "--dst-prefix=b/", "--output=" + output, commit
                },
                directory,
                environment,
                cancellationToken);
            EnsureSuccess(diff.ExitCode, diff.StandardError, "git diff");

            return await File.ReadAllBytesAsync(output, cancellationToken);
        }
        finally
        {
            DeleteQuietly(staging);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChangedFile>> ListAsync(string directory, string patchFile, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        Guard.NotBlank(patchFile, nameof(patchFile));

        var prefix = await GetPrefixAsync(directory, cancellationToken);
        var numbers = await _git.RunInWorkingTreeAsync(ReadArguments(patchFile, prefix, "--numstat", "-z"), directory, _englishMessages, cancellationToken);
        if (numbers.ExitCode != 0)
        {
            return Array.Empty<ChangedFile>();
        }

        var summary = await _git.RunInWorkingTreeAsync(ReadArguments(patchFile, prefix, "--summary"), directory, _englishMessages, cancellationToken);
        var kinds = SummaryLine().Matches(summary.StandardOutput).ToDictionary(
            match => match.Groups["path"].Value,
            match => match.Groups["kind"].Value == "create" ? ChangeKind.Added : ChangeKind.Deleted,
            StringComparer.Ordinal);

        // With -z every entry is "added TAB deleted TAB path" followed by NUL, and paths are never quoted.
        return numbers.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Split('\t', 3))
            .Where(parts => parts.Length == 3)
            .Select(parts => new ChangedFile(kinds.GetValueOrDefault(parts[2], ChangeKind.Modified), WithoutPrefix(parts[2], prefix), false))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<GitPatchApplyResult> ApplyAsync(string directory, GitPatchApplyRequest request, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ArgumentNullException.ThrowIfNull(request);

        var prefix = await GetPrefixAsync(directory, cancellationToken);
        if (FilterArguments(request, prefix) is not { } filter)
        {
            return new GitPatchApplyResult(false, PatchFileFailure.TooManyFiles, Array.Empty<string>(), string.Empty);
        }

        var arguments = new List<string> { "apply", "--verbose", "--whitespace=nowarn" };
        if (request.Reverse)
        {
            arguments.Add("--reverse");
        }

        if (prefix.Length > 0)
        {
            arguments.Add("--directory=" + prefix);
        }

        arguments.AddRange(filter);
        arguments.Add("--");
        arguments.Add(request.PatchFile);

        var result = await _git.RunInWorkingTreeAsync(arguments, directory, _englishMessages, cancellationToken);
        var output = (result.StandardOutput + result.StandardError).Trim();
        if (result.ExitCode == 0)
        {
            return new GitPatchApplyResult(true, null, Array.Empty<string>(), output);
        }

        var (failure, files) = GitApplyErrorReader.Read(result.StandardError);
        return new GitPatchApplyResult(false, failure, files.Select(path => WithoutPrefix(path, prefix)).ToArray(), output);
    }

    /// <inheritdoc />
    public async Task<bool> UnstageAsync(string directory, IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(directory, nameof(directory));
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
        {
            return true;
        }

        var staging = CreateStaging();
        try
        {
            var list = await WritePathspecAsync(staging, paths, cancellationToken);
            var reset = await _git.RunInWorkingTreeAsync(new[] { "reset", "--quiet", "--pathspec-from-file=" + list, "--pathspec-file-nul" }, directory, cancellationToken: cancellationToken);
            return reset.ExitCode == 0;
        }
        finally
        {
            DeleteQuietly(staging);
        }
    }

    /// <summary>
    /// Builds the options that leave out the skipped files: whichever of the two lists is shorter names the files.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="prefix">The folder of the working tree inside the repository.</param>
    /// <returns>The options, or <see langword="null"/> when they do not fit on a command line.</returns>
    private static IReadOnlyList<string>? FilterArguments(GitPatchApplyRequest request, string prefix)
    {
        if (request.Skipped.Count == 0)
        {
            return Array.Empty<string>();
        }

        var include = request.Chosen.Count > 0 && request.Chosen.Count <= request.Skipped.Count;
        var option = include ? "--include=" : "--exclude=";
        var arguments = (include ? request.Chosen : request.Skipped).Select(path => option + EscapePattern(prefix + path)).ToArray();
        return arguments.Sum(argument => argument.Length + 1) <= MAX_PATTERN_CHARACTERS ? arguments : null;
    }

    /// <summary>
    /// Builds the arguments of a <c>git apply</c> that only reads the patch.
    /// </summary>
    /// <param name="patchFile">The patch file.</param>
    /// <param name="prefix">The folder of the working tree inside the repository.</param>
    /// <param name="options">The options that say what to read.</param>
    /// <returns>The arguments.</returns>
    private static IReadOnlyList<string> ReadArguments(string patchFile, string prefix, params string[] options)
    {
        var arguments = new List<string> { "apply" };
        arguments.AddRange(options);
        if (prefix.Length > 0)
        {
            arguments.Add("--directory=" + prefix);
        }

        arguments.Add("--");
        arguments.Add(patchFile);
        return arguments;
    }

    /// <summary>
    /// Keeps the characters of a file name from being read as a pattern.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The pattern that matches exactly that path.</returns>
    private static string EscapePattern(string path) => PatternCharacter().Replace(path, @"\$0");

    /// <summary>
    /// Takes the folder of the working tree off a path Git printed.
    /// </summary>
    /// <param name="path">The path relative to the repository.</param>
    /// <param name="prefix">The folder of the working tree inside the repository.</param>
    /// <returns>The path relative to the working tree.</returns>
    private static string WithoutPrefix(string path, string prefix)
        => prefix.Length > 0 && path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;

    /// <summary>
    /// Fails when a command that must succeed did not.
    /// </summary>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="errors">The standard error.</param>
    /// <param name="command">The name of the command.</param>
    /// <exception cref="GitExecutionException">The command failed.</exception>
    private static void EnsureSuccess(int exitCode, string errors, string command)
    {
        if (exitCode != 0)
        {
            throw new GitExecutionException($"{command} failed with exit code {exitCode}: {errors.Trim()}", exitCode);
        }
    }

    /// <summary>
    /// Writes a list of files for <c>--pathspec-from-file</c>, each one taken literally.
    /// </summary>
    /// <param name="staging">The folder of the list.</param>
    /// <param name="paths">The files.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The path of the list.</returns>
    private static async Task<string> WritePathspecAsync(string staging, IReadOnlyCollection<string> paths, CancellationToken cancellationToken)
    {
        var list = Path.Combine(staging, "paths");
        await File.WriteAllTextAsync(list, string.Join('\0', paths.Select(path => ":(literal)" + path)), new UTF8Encoding(false), cancellationToken);
        return list;
    }

    /// <summary>
    /// Deletes a temporary directory, ignoring failures.
    /// </summary>
    /// <param name="path">The directory.</param>
    private static void DeleteQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary data; best effort cleanup.
        }
    }

    /// <summary>
    /// Creates a temporary directory.
    /// </summary>
    /// <returns>The directory.</returns>
    private string CreateStaging()
    {
        var root = _appData?.Create().TempDirectory ?? Path.Combine(Path.GetTempPath(), "rcynq");
        var staging = Path.Combine(root, "patch-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        return staging;
    }

    /// <summary>
    /// Finds the object directory of the repository, which a private index reads through as an alternate.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The full path.</returns>
    /// <exception cref="GitExecutionException">git reported an error.</exception>
    private async Task<string> GetObjectDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(new[] { "rev-parse", "--git-path", "objects" }, directory, cancellationToken: cancellationToken);
        EnsureSuccess(result.ExitCode, result.StandardError, "git rev-parse");
        return Path.GetFullPath(Path.Combine(directory, result.StandardOutput.TrimEnd('\r', '\n')));
    }

    /// <summary>
    /// Finds the folder of the working tree inside its repository.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The folder with a final <c>/</c>; empty at the root of a repository and in a folder without Git.</returns>
    private async Task<string> GetPrefixAsync(string directory, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(new[] { "rev-parse", "--show-prefix" }, directory, cancellationToken: cancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput.TrimEnd('\r', '\n') : string.Empty;
    }

    /// <summary>Matches a line of <c>git apply --summary</c> about a created or deleted file.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^ (?<kind>create|delete) mode \d+ (?<path>.+?)\r?$", RegexOptions.Multiline)]
    private static partial Regex SummaryLine();

    /// <summary>Matches a character with a meaning in a file pattern of Git.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"[\\*?\[]")]
    private static partial Regex PatternCharacter();
}
