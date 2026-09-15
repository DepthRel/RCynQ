using System.Globalization;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Git.Commands;
using RCynQ.Git.Patches;
using RCynQ.Git.Paths;
using RCynQ.Git.Repository;
using RCynQ.Sync.State;

namespace RCynQ.Sync.Apply;

/// <summary>
/// <see cref="IPatchApplier"/> that uses Git tooling: <c>--check</c>, then <c>--3way</c> in a repository, then <c>--reject</c>.
/// </summary>
/// <remarks>
/// Conflicts left by <c>--3way</c> (unmerged files) or <c>--reject</c> (<c>.rej</c> files) are reported as
/// <see cref="PatchStatus.ConflictPending"/> for manual resolution.
/// </remarks>
public sealed class GitPatchApplier : IPatchApplier
{
    /// <summary>
    /// The git command runner.
    /// </summary>
    private readonly IGitCommandRunner _git;

    /// <summary>
    /// The repository service.
    /// </summary>
    private readonly IGitRepositoryService _repository;

    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly IPathUtility _paths;

    /// <summary>
    /// The node data directories, or <see langword="null"/> to write diff files to the system temporary directory.
    /// </summary>
    private readonly IAppDataFactory? _appData;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitPatchApplier"/> class.
    /// </summary>
    /// <param name="git">The git command runner.</param>
    /// <param name="repository">The repository service.</param>
    /// <param name="paths">The path utility.</param>
    /// <param name="appData">The node data directories; diff files handed to Git are written to its <c>tmp</c> directory.</param>
    public GitPatchApplier(IGitCommandRunner git, IGitRepositoryService repository, IPathUtility paths, IAppDataFactory? appData = null)
    {
        _git = git;
        _repository = repository;
        _paths = paths;
        _appData = appData;
    }

    /// <inheritdoc />
    public async Task<PatchApplyResult> ApplyAsync(PatchEnvelope envelope, string targetDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var root = Path.GetFullPath(Guard.NotBlank(targetDirectory, nameof(targetDirectory)));
        if (envelope.Diff.Length == 0)
        {
            return PatchApplyResult.Applied(envelope.Id, 0);
        }

        var tempRoot = _appData?.Create().TempDirectory ?? Path.Combine(Path.GetTempPath(), "rcynq");
        var diffFile = Path.Combine(tempRoot, $"apply-{envelope.Id:N}-{Guid.NewGuid():N}.diff");
        Directory.CreateDirectory(Path.GetDirectoryName(diffFile)!);
        await File.WriteAllBytesAsync(diffFile, envelope.Diff, cancellationToken);
        try
        {
            var isRepository = await _repository.IsWorkTreeAsync(root, cancellationToken);
            var baseArguments = new List<string> { "apply", "--whitespace=nowarn" };
            if (isRepository)
            {
                var prefix = await _git.RunAsync(new[] { "rev-parse", "--show-prefix" }, root, cancellationToken: cancellationToken);
                var directory = prefix.StandardOutput.Trim().TrimEnd('/');
                if (prefix.ExitCode == 0 && directory.Length > 0)
                {
                    baseArguments.Add("--directory=" + directory);
                }
            }

            var check = await RunApplyAsync(baseArguments, "--check", diffFile, root, cancellationToken);
            if (check.ExitCode == 0)
            {
                var apply = await RunApplyAsync(baseArguments, null, diffFile, root, cancellationToken);
                return apply.ExitCode == 0
                    ? PatchApplyResult.Applied(envelope.Id, envelope.Manifest.Count)
                    : PatchApplyResult.Failed(envelope.Id, $"git apply failed: {apply.StandardError.Trim()}");
            }

            if (isRepository)
            {
                var threeWay = await RunApplyAsync(baseArguments, "--3way", diffFile, root, cancellationToken);
                if (threeWay.ExitCode == 0)
                {
                    return PatchApplyResult.Applied(envelope.Id, envelope.Manifest.Count);
                }

                var unmerged = await _repository.GetUnmergedFilesAsync(root, cancellationToken);
                if (unmerged.Count > 0)
                {
                    var hashes = envelope.Manifest.ToDictionary(change => change.Path, change => change.Sha256, StringComparer.Ordinal);
                    return PatchApplyResult.Conflict(
                        envelope.Id,
                        unmerged.Select(path => new ConflictFile(path, null, hashes.GetValueOrDefault(path), "Resolve the conflict markers, then stage the file.")).ToArray());
                }
            }

            var started = DateTime.UtcNow.AddSeconds(-2);
            var reject = await RunApplyAsync(baseArguments, "--reject", diffFile, root, cancellationToken);
            if (reject.ExitCode == 0)
            {
                return PatchApplyResult.Applied(envelope.Id, envelope.Manifest.Count);
            }

            var rejected = envelope.Manifest
                .Where(change =>
                {
                    var rejectFile = _paths.ResolveWithinRoot(root, change.Path) + ".rej";
                    return File.Exists(rejectFile) && File.GetLastWriteTimeUtc(rejectFile) >= started;
                })
                .Select(change => new ConflictFile(change.Path, null, change.Sha256, $"Rejected hunks were written to {change.Path}.rej."))
                .ToArray();

            return rejected.Length > 0
                ? PatchApplyResult.Conflict(envelope.Id, rejected)
                : PatchApplyResult.Failed(envelope.Id, $"git apply failed: {reject.StandardError.Trim()}");
        }
        finally
        {
            if (File.Exists(diffFile))
            {
                File.Delete(diffFile);
            }
        }
    }

    /// <summary>
    /// Runs one <c>git apply</c> variant.
    /// </summary>
    /// <param name="baseArguments">The shared arguments.</param>
    /// <param name="mode">The mode flag, or <see langword="null"/> for a plain apply.</param>
    /// <param name="diffFile">The diff file.</param>
    /// <param name="root">The working directory.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result.</returns>
    private Task<ProcessResult> RunApplyAsync(IReadOnlyList<string> baseArguments, string? mode, string diffFile, string root, CancellationToken cancellationToken)
    {
        var arguments = baseArguments.ToList();
        if (mode is not null)
        {
            arguments.Add(mode);
        }

        arguments.Add(diffFile);
        return _git.RunAsync(arguments, root, cancellationToken: cancellationToken);
    }
}
