using RCynQ.Core.Exceptions;
using RCynQ.Core.Validation;
using RCynQ.Git.Commands;

namespace RCynQ.Git.Patches.Diff;

/// <summary>
/// Default <see cref="IGitDiffBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// The staging directories are literally named <c>a</c> and <c>b</c> and git runs with <c>--no-prefix</c> inside the
/// staging directory, so the output carries the standard <c>a/path</c> / <c>b/path</c> headers that <c>git apply</c>
/// expects with its default <c>-p1</c>.
/// </para>
/// <para>
/// The diff is written with <c>--output</c> and read as bytes, so file content in any encoding is preserved.
/// Exit code 0 (no differences) and 1 (differences) are success; exit code 1 without any output means git could not
/// access a path and is treated as an error, as is any exit code greater than 1.
/// </para>
/// </remarks>
public sealed class GitDiffBuilder : IGitDiffBuilder
{
    /// <summary>
    /// The name of the diff output file inside the staging directory.
    /// </summary>
    public const string OutputFileName = "patch.diff";

    /// <summary>
    /// The git command runner.
    /// </summary>
    private readonly IGitCommandRunner _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitDiffBuilder"/> class.
    /// </summary>
    /// <param name="git">The git command runner.</param>
    public GitDiffBuilder(IGitCommandRunner git)
    {
        _git = git;
    }

    /// <inheritdoc />
    public async Task<byte[]> BuildAsync(string stagingDirectory, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(Guard.NotBlank(stagingDirectory, nameof(stagingDirectory)));
        Directory.CreateDirectory(Path.Combine(root, "a"));
        Directory.CreateDirectory(Path.Combine(root, "b"));
        var output = Path.Combine(root, OutputFileName);
        if (File.Exists(output))
        {
            File.Delete(output);
        }

        var result = await _git.RunAsync(
            new[]
            {
                "diff", "--no-index", "--binary", "--no-prefix", "--no-renames", "--no-color", "--no-ext-diff", "--no-textconv",
                "--output=" + output, "--", "a", "b"
            },
            root,
            cancellationToken: cancellationToken);

        var bytes = File.Exists(output) ? await File.ReadAllBytesAsync(output, cancellationToken) : Array.Empty<byte>();
        if (result.ExitCode == 0 || (result.ExitCode == 1 && bytes.Length > 0))
        {
            return bytes;
        }

        throw new GitExecutionException(
            $"git diff --no-index failed with exit code {result.ExitCode}: {result.StandardError.Trim()}",
            result.ExitCode);
    }
}
