using System.Text;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Git.Hashing;
using RCynQ.Localization;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Services;

namespace RCynQ.Sync.Partial;

/// <summary>
/// Shows what a changed file brings against the version of the peer, and keeps the choice of the changes that go.
/// </summary>
/// <remarks>
/// Every call reads the two files of one path and nothing else: no walk of the working tree and no wait for the working
/// tree lock, so a diff opens at once. Whatever a choice was made on is checked again when the patch is sent, under the lock.
/// </remarks>
public sealed class PartialChangeService
{
    /// <summary>How many unchanged lines are shown before and after a change.</summary>
    public const int CONTEXT_LINES = 3;

    /// <summary>How long the files handed to an external tool are kept.</summary>
    private static readonly TimeSpan _comparisonLifetime = TimeSpan.FromDays(1);

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The path utility.</summary>
    private readonly IPathUtility _pathUtility;

    /// <summary>The stored choices.</summary>
    private readonly PartialChangeStore _store;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartialChangeService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="pathUtility">The path utility.</param>
    /// <param name="store">The stored choices.</param>
    /// <param name="timeProvider">The clock.</param>
    public PartialChangeService(ILocalizer localizer, NodeConfig node, SyncPaths paths, IPathUtility pathUtility, PartialChangeStore store, TimeProvider timeProvider)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _pathUtility = pathUtility ?? throw new ArgumentNullException(nameof(pathUtility));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Builds the diff of a file against the version of the peer, with the changes that go now.
    /// </summary>
    /// <param name="path">The path relative to the working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The diff.</returns>
    /// <exception cref="ArgumentException">The path is blank or leads out of the working tree.</exception>
    /// <exception cref="SyncBridgeException">The node has no working tree.</exception>
    public async Task<FileDiffResponse> GetDiffAsync(string path, CancellationToken cancellationToken = default)
    {
        var (normalized, baseSide, workSide) = await ReadAsync(path, cancellationToken);
        var availability = Availability(baseSide, workSide);
        if (availability != DiffAvailability.Text)
        {
            return new FileDiffResponse(normalized, availability, PartialChangeState.None, false, false, baseSide.Sha256, workSide.Sha256, Array.Empty<DiffBlockDto>());
        }

        var diff = FileDiff.Create(baseSide.Bytes ?? Array.Empty<byte>(), workSide.Bytes ?? Array.Empty<byte>());
        var canChoose = workSide.Exists && diff.Blocks.Count > 0;
        var choice = await FittingChoiceAsync(normalized, baseSide.Sha256, cancellationToken);
        var state = choice is null ? PartialChangeState.None : PartialChangeStore.StateOf(choice, workSide.Sha256);

        IReadOnlyList<int>? chosen;
        if (choice is null)
        {
            chosen = diff.Blocks.Select(block => block.Index).ToArray();
        }
        else if (choice.Blocks is not null && state == PartialChangeState.Partial)
        {
            chosen = choice.Blocks;
        }
        else
        {
            chosen = diff.FindChoice(choice.Content);
        }

        var blocks = BuildBlocks(diff, (chosen ?? Array.Empty<int>()).ToHashSet());
        return new FileDiffResponse(normalized, availability, state, canChoose, choice is not null && chosen is null, baseSide.Sha256, workSide.Sha256, blocks);
    }

    /// <summary>
    /// Writes the files of a diff for an external tool: a read-only copy of the version of the peer, and the working file
    /// itself or, for a choice, an editable copy of the version that goes now.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The files.</returns>
    /// <exception cref="ArgumentException">The path is blank or leads out of the working tree.</exception>
    /// <exception cref="SyncBridgeException">The node has no working tree, or no part of the file can be chosen.</exception>
    public async Task<CompareFilesResponse> PrepareCompareAsync(CompareFilesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (normalized, baseSide, workSide) = await ReadAsync(request.Path, cancellationToken);
        var availability = Availability(baseSide, workSide);
        if (request.ForChoice && (availability != DiffAvailability.Text || !workSide.Exists))
        {
            throw new SyncBridgeException(_localizer.Format("Changes.CannotChoose", normalized));
        }

        var directory = CreateComparisonDirectory();
        var name = Path.GetFileName(normalized);
        var baseFile = await CopyAsync(Path.Combine(directory, "base", name), baseSide, cancellationToken);

        string workFile;
        if (request.ForChoice)
        {
            var choice = await FittingChoiceAsync(normalized, baseSide.Sha256, cancellationToken);
            workFile = await WriteCopyAsync(Path.Combine(directory, "choice", name), choice?.Content ?? workSide.Bytes!, readOnly: false, cancellationToken);
        }
        else if (workSide.Exists && !workSide.IsLink)
        {
            workFile = workSide.FullPath;
        }
        else
        {
            workFile = await CopyAsync(Path.Combine(directory, "work", name), workSide, cancellationToken);
        }

        return new CompareFilesResponse(normalized, availability, baseFile, workFile, baseSide.Sha256, workSide.Sha256);
    }

    /// <summary>
    /// Keeps the changes of a file the user chose in the diff.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Whether the whole file, a part of it or nothing goes.</returns>
    /// <exception cref="ArgumentException">The path is blank or leads out of the working tree.</exception>
    /// <exception cref="InvalidOperationException">The file or the version of the peer changed since the diff was shown.</exception>
    /// <exception cref="SyncBridgeException">The node has no working tree, or no part of the file can be chosen.</exception>
    public async Task<PartialChoiceResult> ChooseAsync(ChooseChangesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Blocks);

        var (normalized, baseSide, workSide) = await ReadForChoiceAsync(request.Path, request.BaseSha256, cancellationToken);
        if (!string.Equals(workSide.Sha256, request.WorkdirSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(LocalizedError.Message("Changes.ChangedSinceShown", normalized, PeerRole));
        }

        var diff = FileDiff.Create(baseSide.Bytes ?? Array.Empty<byte>(), workSide.Bytes!);
        var chosen = diff.Blocks.Select(block => block.Index).Where(request.Blocks.Contains).ToArray();
        var outcome = chosen.Length == 0 ? PartialChoiceOutcome.Nothing : chosen.Length == diff.Blocks.Count ? PartialChoiceOutcome.Whole : PartialChoiceOutcome.Partial;
        await KeepAsync(normalized, outcome, baseSide.Sha256, workSide.Sha256!, () => diff.Compose(chosen), chosen, cancellationToken);
        return new PartialChoiceResult(normalized, outcome);
    }

    /// <summary>
    /// Keeps the version of a file the user left in an external tool.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Whether the whole file, a part of it or nothing goes.</returns>
    /// <exception cref="ArgumentException">The path is blank or leads out of the working tree, or the content is too large.</exception>
    /// <exception cref="InvalidOperationException">The version of the peer changed since the files were prepared.</exception>
    /// <exception cref="SyncBridgeException">The node has no working tree, or no part of the file can be chosen.</exception>
    public async Task<PartialChoiceResult> SetContentAsync(SetPartialContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Content);
        if (request.Content.Length > ApiLimits.MAX_DIFF_FILE_BYTES)
        {
            throw new ArgumentException(LocalizedError.Message("Changes.TooLarge", request.Path), nameof(request));
        }

        var (normalized, baseSide, workSide) = await ReadForChoiceAsync(request.Path, request.BaseSha256, cancellationToken);
        var content = request.Content;
        var outcome = content.AsSpan().SequenceEqual(workSide.Bytes) ? PartialChoiceOutcome.Whole
            : content.AsSpan().SequenceEqual(baseSide.Bytes ?? Array.Empty<byte>()) ? PartialChoiceOutcome.Nothing
            : PartialChoiceOutcome.Partial;

        // The working file the copy was made of is what the choice was made on; a file edited meanwhile shows as edited since.
        var workdirSha256 = request.WorkdirSha256 ?? workSide.Sha256!;
        var blocks = string.Equals(workdirSha256, workSide.Sha256, StringComparison.Ordinal)
            ? FileDiff.Create(baseSide.Bytes ?? Array.Empty<byte>(), workSide.Bytes!).FindChoice(content)
            : null;
        await KeepAsync(normalized, outcome, baseSide.Sha256, workdirSha256, () => content, blocks, cancellationToken);
        return new PartialChoiceResult(normalized, outcome);
    }

    /// <summary>
    /// Lets the whole file go again.
    /// </summary>
    /// <param name="path">The path relative to the working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when there was a choice.</returns>
    /// <exception cref="ArgumentException">The path is blank or leads out of the working tree.</exception>
    /// <exception cref="SyncBridgeException">The node has no working tree.</exception>
    public Task<bool> ResetAsync(string path, CancellationToken cancellationToken = default)
    {
        PatchOperations.EnsureParticipant(_node, _localizer);
        return _store.RemoveAsync(Normalize(path), cancellationToken);
    }

    /// <summary>
    /// Stores or forgets a choice by its outcome.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="baseSha256">The hash of the version of the peer.</param>
    /// <param name="workdirSha256">The hash of the working file the choice was made on.</param>
    /// <param name="content">Builds the version that goes.</param>
    /// <param name="blocks">The chosen changes, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the choice is stored or forgotten.</returns>
    private async Task KeepAsync(string path, PartialChoiceOutcome outcome, string? baseSha256, string workdirSha256, Func<byte[]> content, IReadOnlyList<int>? blocks, CancellationToken cancellationToken)
    {
        if (outcome != PartialChoiceOutcome.Partial)
        {
            // Nothing chosen leaves the file out of the next patch: the client takes the check mark off.
            await _store.RemoveAsync(path, cancellationToken);
            return;
        }

        await _store.SaveAsync(new PartialChange(_store.RepositoryId, path, baseSha256, workdirSha256, content(), blocks, _timeProvider.GetUtcNow()), cancellationToken);
    }

    /// <summary>
    /// Reads both versions of a file a choice is about to be made for, and checks that one can be made.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="baseSha256">The hash of the version of the peer the user saw.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The normalized path and both versions.</returns>
    private async Task<(string Path, FileSide Base, FileSide Work)> ReadForChoiceAsync(string path, string? baseSha256, CancellationToken cancellationToken)
    {
        var read = await ReadAsync(path, cancellationToken);
        if (Availability(read.Base, read.Work) != DiffAvailability.Text || !read.Work.Exists)
        {
            throw new SyncBridgeException(_localizer.Format("Changes.CannotChoose", read.Path));
        }

        if (!string.Equals(read.Base.Sha256, baseSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(LocalizedError.Message("Changes.ChangedSinceShown", read.Path, PeerRole));
        }

        return read;
    }

    /// <summary>
    /// Finds the stored choice of a file when it was made on the version of the peer the base holds now.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="baseSha256">The hash of the version of the peer now.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The choice, or <see langword="null"/>.</returns>
    private async Task<PartialChange?> FittingChoiceAsync(string path, string? baseSha256, CancellationToken cancellationToken)
    {
        var choice = await _store.GetAsync(path, cancellationToken);
        return choice is not null && string.Equals(choice.BaseSha256, baseSha256, StringComparison.Ordinal) ? choice : null;
    }

    /// <summary>
    /// Reads the version of the peer and the working file of a path.
    /// </summary>
    /// <param name="path">The path relative to the working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The normalized path and both versions.</returns>
    private async Task<(string Path, FileSide Base, FileSide Work)> ReadAsync(string path, CancellationToken cancellationToken)
    {
        PatchOperations.EnsureParticipant(_node, _localizer);
        var normalized = Normalize(path);

        var baseSide = await FileSide.ReadAsync(_pathUtility.ResolveWithinRoot(_paths.SyncBase, normalized), cancellationToken);
        var workSide = await FileSide.ReadAsync(_pathUtility.ResolveWithinRoot(_paths.Workdir, normalized), cancellationToken);
        return (normalized, baseSide, workSide);
    }

    /// <summary>
    /// Checks a path from a request and brings it to the form of the list of changes.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The path relative to the working tree with <c>/</c> separators.</returns>
    /// <exception cref="ArgumentException">The path is blank or leads out of the working tree.</exception>
    private string Normalize(string path) => ChangePath.Normalize(_pathUtility, _paths.Workdir, path);

    /// <summary>Gets the role of the node the changes go to: the Origin of a Workstation, the Workstation of an Origin.</summary>
    private NodeRole PeerRole => _node.Role == NodeRole.Workstation ? NodeRole.Origin : NodeRole.Workstation;

    /// <summary>
    /// Tells whether the diff of two versions is shown line by line.
    /// </summary>
    /// <param name="baseSide">The version of the peer.</param>
    /// <param name="workSide">The working file.</param>
    /// <returns>The availability.</returns>
    private static DiffAvailability Availability(FileSide baseSide, FileSide workSide)
    {
        if (baseSide.IsLink || workSide.IsLink)
        {
            return DiffAvailability.Link;
        }

        if (baseSide.TooLarge || workSide.TooLarge)
        {
            return DiffAvailability.TooLarge;
        }

        if (string.Equals(baseSide.Sha256, workSide.Sha256, StringComparison.Ordinal))
        {
            return DiffAvailability.Same;
        }

        return TextContent.IsBinary(baseSide.Bytes) || TextContent.IsBinary(workSide.Bytes) ? DiffAvailability.Binary : DiffAvailability.Text;
    }

    /// <summary>
    /// Builds the changes to show, each with the unchanged lines around it; a line between two changes is shown once.
    /// </summary>
    /// <param name="diff">The diff.</param>
    /// <param name="chosen">The changes that go.</param>
    /// <returns>The changes.</returns>
    private static IReadOnlyList<DiffBlockDto> BuildBlocks(FileDiff diff, IReadOnlySet<int> chosen)
    {
        var blocks = new List<DiffBlockDto>(diff.Blocks.Count);
        var shownUntil = 0;
        for (var index = 0; index < diff.Blocks.Count; index++)
        {
            var block = diff.Blocks[index];
            var lines = new List<DiffLineDto>();

            var before = Math.Max(block.BaseStart - CONTEXT_LINES, shownUntil);
            AddContext(lines, diff, before, block.BaseStart, block.WorkStart - block.BaseStart);
            for (var line = block.BaseStart; line < block.BaseStart + block.BaseCount; line++)
            {
                lines.Add(new DiffLineDto(DiffLineKind.Removed, line + 1, null, diff.Base.Text(line)));
            }

            for (var line = block.WorkStart; line < block.WorkStart + block.WorkCount; line++)
            {
                lines.Add(new DiffLineDto(DiffLineKind.Added, null, line + 1, diff.Work.Text(line)));
            }

            var end = block.BaseStart + block.BaseCount;
            var next = index + 1 < diff.Blocks.Count ? diff.Blocks[index + 1].BaseStart : diff.Base.Count;
            var after = Math.Min(end + CONTEXT_LINES, next);
            AddContext(lines, diff, end, after, block.WorkStart + block.WorkCount - end);
            shownUntil = after;

            blocks.Add(new DiffBlockDto(block.Index, block.BaseStart, block.BaseCount, block.WorkStart, block.WorkCount, chosen.Contains(block.Index), lines));
        }

        return blocks;
    }

    /// <summary>
    /// Adds unchanged lines of the version of the peer.
    /// </summary>
    /// <param name="lines">The lines to add to.</param>
    /// <param name="diff">The diff.</param>
    /// <param name="from">The first line of the version of the peer.</param>
    /// <param name="to">The line after the last one.</param>
    /// <param name="shift">How far the same lines are in the working file.</param>
    private static void AddContext(List<DiffLineDto> lines, FileDiff diff, int from, int to, int shift)
    {
        for (var line = from; line < to; line++)
        {
            lines.Add(new DiffLineDto(DiffLineKind.Context, line + 1, line + shift + 1, diff.Base.Text(line)));
        }
    }

    /// <summary>
    /// Creates the folder of the files of one diff, removing the folders older than a day.
    /// </summary>
    /// <returns>The full path of the folder.</returns>
    private string CreateComparisonDirectory()
    {
        var root = _paths.Data.ComparisonsDirectory;
        var cutoff = _timeProvider.GetUtcNow() - _comparisonLifetime;
        if (Directory.Exists(root))
        {
            foreach (var old in Directory.GetDirectories(root).Where(directory => Directory.GetCreationTimeUtc(directory) < cutoff.UtcDateTime))
            {
                DeleteQuietly(old);
            }
        }

        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Writes a read-only copy of a version for the eyes of the user: its bytes, the file itself when it was too large to
    /// read, or an empty file when there is none.
    /// </summary>
    /// <param name="file">The full path of the copy.</param>
    /// <param name="side">The version.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The full path of the copy.</returns>
    private static async Task<string> CopyAsync(string file, FileSide side, CancellationToken cancellationToken)
    {
        if (side.Bytes is null && side.Exists)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.Copy(side.FullPath, file);
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
            return file;
        }

        return await WriteCopyAsync(file, side.Bytes ?? Array.Empty<byte>(), readOnly: true, cancellationToken);
    }

    /// <summary>
    /// Writes a copy of a version.
    /// </summary>
    /// <param name="file">The full path of the copy.</param>
    /// <param name="content">The content.</param>
    /// <param name="readOnly">Whether the copy is marked read-only, so a tool does not offer to change it.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The full path of the copy.</returns>
    private static async Task<string> WriteCopyAsync(string file, byte[] content, bool readOnly, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, content, cancellationToken);
        if (readOnly)
        {
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        }

        return file;
    }

    /// <summary>
    /// Removes a folder with everything in it, leaving it when a tool still holds a file.
    /// </summary>
    /// <param name="directory">The folder.</param>
    private static void DeleteQuietly(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A tool may still hold a file; the folder goes with a later diff.
        }
    }

    /// <summary>
    /// One version of a file as it was read.
    /// </summary>
    /// <param name="FullPath">The full path.</param>
    /// <param name="Exists">Whether there is a file or a link at the path.</param>
    /// <param name="IsLink">Whether it is a symbolic link.</param>
    /// <param name="TooLarge">Whether it is larger than <see cref="ApiLimits.MAX_DIFF_FILE_BYTES"/>; its bytes are not read then.</param>
    /// <param name="Bytes">The content (the target of a link as UTF-8), or <see langword="null"/> when there is none or it was not read.</param>
    /// <param name="Sha256">The SHA-256 of the content, or <see langword="null"/> when there is none.</param>
    private sealed record FileSide(string FullPath, bool Exists, bool IsLink, bool TooLarge, byte[]? Bytes, string? Sha256)
    {
        /// <summary>
        /// Reads a version.
        /// </summary>
        /// <param name="fullPath">The full path.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The version.</returns>
        public static async Task<FileSide> ReadAsync(string fullPath, CancellationToken cancellationToken)
        {
            var info = new FileInfo(fullPath);
            if (info.LinkTarget is { } target)
            {
                var bytes = Encoding.UTF8.GetBytes(target);
                return new FileSide(fullPath, true, true, false, bytes, DefaultStateHasher.Sha256Hex(bytes));
            }

            if (!info.Exists)
            {
                return new FileSide(fullPath, false, false, false, null, null);
            }

            if (info.Length > ApiLimits.MAX_DIFF_FILE_BYTES)
            {
                return new FileSide(fullPath, true, false, true, null, await DefaultStateHasher.Sha256FileAsync(fullPath, cancellationToken));
            }

            // The file may be open in an editor; sharing for writing keeps a running IDE from blocking the read.
            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
            using var buffer = new MemoryStream((int)info.Length);
            await stream.CopyToAsync(buffer, cancellationToken);
            var content = buffer.ToArray();
            return new FileSide(fullPath, true, false, false, content, DefaultStateHasher.Sha256Hex(content));
        }
    }
}
