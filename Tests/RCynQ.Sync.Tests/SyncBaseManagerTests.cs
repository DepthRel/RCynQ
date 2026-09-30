using FluentAssertions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Sync.SyncBase;
using RCynQ.Sync.Tests.Fixtures;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests for <see cref="SyncBaseManager"/> on real temporary repositories. Tests that need Git return early when it is not installed.
/// </summary>
public sealed class SyncBaseManagerTests : IDisposable
{
    /// <summary>The fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The Git runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(), new GitOptions());

    /// <summary>
    /// Verifies that a Git working tree that was never pulled explains why it is not ready and does not offer a manual baseline.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Source_Repository_Without_A_Pull_Is_Not_Ready()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitAsync(_fixture.SourceWork);

        var status = await EnsureAsync(_fixture.SourceNode);

        status.IsReady.Should().BeFalse();
        status.CanCreateManual.Should().BeFalse("a Git working tree gets its base from Git");
        status.Problem.Should().Contain("main").And.Contain("Pull");
    }

    /// <summary>
    /// Verifies that a working tree without Git keeps the manual snapshot of earlier versions.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Working_Tree_Without_Git_Uses_A_Manual_Snapshot()
    {
        var before = await EnsureAsync(_fixture.SourceNode);
        before.IsReady.Should().BeFalse();
        before.CanCreateManual.Should().BeTrue();

        await _fixture.SourceSyncBase.RebuildAsync();
        var after = await EnsureAsync(_fixture.SourceNode);

        after.Mode.Should().Be(SyncBaseMode.Manual);
        SyncFixture.Read(_fixture.SourcePaths.SyncBase, "a.txt").Should().Be("v1\n");
    }

    /// <summary>
    /// Verifies that the manual snapshot of an older version on a Git repository is replaced by the commit: it says nothing
    /// reliable about what the peer has.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Legacy_Snapshot_Is_Replaced_By_The_Commit()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitAsync(_fixture.DestWork);
        SyncFixture.Write(_fixture.DestPaths.SyncBase, "a.txt", "received earlier\n");

        var status = await EnsureAsync(_fixture.DestNode);

        status.Mode.Should().Be(SyncBaseMode.Git);
        SyncFixture.Read(_fixture.DestPaths.SyncBase, "a.txt").Should().Be("v1\n");
        SyncFixture.Read(_fixture.DestPaths.SyncBase, "b.txt").Should().Be("keep\n");
        (await MetadataAsync(_fixture.DestNode)).Exchanged.Should().BeEmpty();
    }

    /// <summary>
    /// Regression (reported twice): a node that had an old manual snapshot, taken when the repository was smaller or older,
    /// offered every file added since as a change to send, on every start, forever. The old snapshot says nothing reliable about
    /// what the peer has, so it must not leak into the Git base.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Stale_Legacy_Snapshot_Does_Not_Make_Committed_Files_Pending()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.DestWork, "c.txt", "added after the old snapshot\n");
        SyncFixture.Write(_fixture.DestWork, "sub/d.txt", "so was this\n");
        await InitAsync(_fixture.DestWork);
        // The old snapshot only knows a.txt, and with an outdated content.
        Directory.Delete(_fixture.DestPaths.SyncBase, recursive: true);
        SyncFixture.Write(_fixture.DestPaths.SyncBase, "a.txt", "very old\n");

        var tracker = _fixture.CreateTracker(_fixture.DestNode);
        var snapshot = await tracker.CaptureAsync();
        var again = await tracker.CaptureAsync();

        snapshot.SyncBase.Mode.Should().Be(SyncBaseMode.Git);
        snapshot.PendingChanges.Should().BeEmpty("the working tree is exactly the committed state");
        again.PendingChanges.Should().BeEmpty();
        SyncFixture.Read(_fixture.DestPaths.SyncBase, "c.txt").Should().Be("added after the old snapshot\n");
        SyncFixture.Read(_fixture.DestPaths.SyncBase, "a.txt").Should().Be("v1\n");
    }

    /// <summary>
    /// Verifies that the metadata written by an earlier version (which recorded exchanged files as a plain list and could hold
    /// anything, including every file of the repository) is discarded instead of carried over.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Metadata_Of_An_Earlier_Version_Is_Discarded()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.DestWork, "c.txt", "committed\n");
        await InitAsync(_fixture.DestWork);
        var head = (await FirstCommitAsync(_fixture.DestWork));
        Directory.Delete(_fixture.DestPaths.SyncBase, recursive: true);
        SyncFixture.Write(_fixture.DestPaths.SyncBase, "a.txt", "delivered long ago\n");
        var metadataPath = Path.Combine(_fixture.Root, "dest-data", "snapshots", "base.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        await File.WriteAllTextAsync(metadataPath, $$"""{"mode":"Git","branch":"main","commit":"{{head}}","exchanged":["a.txt","b.txt","c.txt"],"format":2}""");

        var tracker = _fixture.CreateTracker(_fixture.DestNode);
        var snapshot = await tracker.CaptureAsync();

        snapshot.PendingChanges.Should().BeEmpty();
        SyncFixture.Read(_fixture.DestPaths.SyncBase, "b.txt").Should().Be("keep\n");
        SyncFixture.Read(_fixture.DestPaths.SyncBase, "c.txt").Should().Be("committed\n");
        (await MetadataAsync(_fixture.DestNode)).Exchanged.Should().BeEmpty();
    }

    /// <summary>
    /// Regression (the Source of the second report): an earlier version wrote the content of a patch of Dest into the sync base of
    /// Source and remembered every path as exchanged, so files that Source's commit does not have showed up as deleted, and a
    /// changed file as modified, on a working tree that <c>git status</c> reports as clean.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Source_With_Polluted_Base_Of_An_Earlier_Version_Has_Nothing_Pending()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitAsync(_fixture.SourceWork);
        await RunAsync(_fixture.SourceWork, "update-ref", "refs/remotes/dest/main", "HEAD");
        var head = await FirstCommitAsync(_fixture.SourceWork);
        SyncFixture.Write(_fixture.SourcePaths.SyncBase, "a.txt", "content of Dest, another version\n");
        SyncFixture.Write(_fixture.SourcePaths.SyncBase, "b.txt", "keep\n");
        SyncFixture.Write(_fixture.SourcePaths.SyncBase, "Portal/Jobs/Job.cs", "exists on Dest, not on this branch\n");
        var metadataPath = Path.Combine(_fixture.Root, "source-data", "snapshots", "base.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        await File.WriteAllTextAsync(metadataPath, $$"""{"mode":"Git","branch":"main","commit":"{{head}}","exchanged":["a.txt","b.txt","Portal/Jobs/Job.cs"],"format":2}""");

        var snapshot = await _fixture.CreateTracker(_fixture.SourceNode).CaptureAsync();

        snapshot.SyncBase.Mode.Should().Be(SyncBaseMode.Git);
        snapshot.PendingChanges.Should().BeEmpty("git status is clean: there is nothing the peer lacks");
        File.Exists(Path.Combine(_fixture.SourcePaths.SyncBase, "Portal", "Jobs", "Job.cs")).Should().BeFalse();
    }

    /// <summary>
    /// Regression: files that came from Dest in a patch and were then discarded on Source are still remembered as exchanged, and the
    /// sync base still holds Dest's version. <c>git status</c> is clean, so nothing may be listed as a change to send.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Discarded_Files_From_Dest_Are_Not_Listed_Because_Git_Sees_No_Change()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitAsync(_fixture.SourceWork);
        await RunAsync(_fixture.SourceWork, "update-ref", "refs/remotes/dest/main", "HEAD");
        var head = await FirstCommitAsync(_fixture.SourceWork);
        SyncFixture.Write(_fixture.SourcePaths.SyncBase, "a.txt", "delivered by Dest, discarded on Source\n");
        var metadataPath = Path.Combine(_fixture.Root, "source-data", "snapshots", "base.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        await File.WriteAllTextAsync(metadataPath, $$"""{"mode":"Git","branch":"main","commit":"{{head}}","exchanged":{"a.txt":null},"format":{{SyncBaseMetadata.CurrentFormat}}}""");
        var tracker = _fixture.CreateTracker(_fixture.SourceNode);

        var clean = await tracker.CaptureAsync();

        clean.PendingChanges.Should().BeEmpty("git status is clean");
        clean.ChangedFiles.Should().BeEmpty();

        SyncFixture.Write(_fixture.SourceWork, "b.txt", "edited\n");
        var edited = await tracker.CaptureAsync();
        edited.PendingChanges.Select(change => change.Path).Should().Equal("b.txt");
        edited.ChangedFiles.Select(file => file.Path).Should().Equal("b.txt");
    }

    /// <summary>
    /// Verifies that a record of a delivered file is only trusted while the base still holds what was recorded.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Exchange_Record_That_Does_Not_Match_The_Base_Is_Dropped()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        Directory.Delete(_fixture.DestPaths.SyncBase, recursive: true);
        await InitAsync(_fixture.DestWork);
        (await EnsureAsync(_fixture.DestNode)).Mode.Should().Be(SyncBaseMode.Git);
        await ReceiveAsync(_fixture.DestNode, "a.txt", "from Source\n");
        // Somebody (an older version, a crash, a hand edit) changes the base behind the record's back.
        SyncFixture.Write(_fixture.DestPaths.SyncBase, "a.txt", "something else\n");

        SyncFixture.Write(_fixture.DestWork, "b.txt", "committed on Dest\n");
        await CommitAsync(_fixture.DestWork, "b.txt");
        await EnsureAsync(_fixture.DestNode);

        SyncFixture.Read(_fixture.DestPaths.SyncBase, "a.txt").Should().Be("v1\n", "the record is not trusted, the commit is");
        (await MetadataAsync(_fixture.DestNode)).Exchanged.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that Dest keeps a received file across its own commits, and forgets it once a commit has exactly that content.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Dest_Keeps_Exchanged_Files_Until_A_Commit_Has_Them()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        Directory.Delete(_fixture.DestPaths.SyncBase, recursive: true);
        await InitAsync(_fixture.DestWork);
        (await EnsureAsync(_fixture.DestNode)).Mode.Should().Be(SyncBaseMode.Git);
        await ReceiveAsync(_fixture.DestNode, "a.txt", "from Source\n");

        SyncFixture.Write(_fixture.DestWork, "b.txt", "committed on Dest\n");
        await CommitAsync(_fixture.DestWork, "b.txt");
        await EnsureAsync(_fixture.DestNode);

        SyncFixture.Read(_fixture.DestPaths.SyncBase, "a.txt").Should().Be("from Source\n", "Source still has it and Dest did not commit it");
        SyncFixture.Read(_fixture.DestPaths.SyncBase, "b.txt").Should().Be("committed on Dest\n");
        (await MetadataAsync(_fixture.DestNode)).Exchanged.Keys.Should().Equal("a.txt");

        SyncFixture.Write(_fixture.DestWork, "a.txt", "from Source\n");
        await CommitAsync(_fixture.DestWork, "a.txt");
        await EnsureAsync(_fixture.DestNode);

        SyncFixture.Read(_fixture.DestPaths.SyncBase, "a.txt").Should().Be("from Source\n");
        (await MetadataAsync(_fixture.DestNode)).Exchanged.Should().BeEmpty("the commit has it now");
    }

    /// <summary>
    /// Verifies that when Source moves to a newer commit of Dest, the files that commit changed follow the commit, and the other
    /// exchanged files are kept.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Source_Lets_A_Pulled_Commit_Replace_The_Files_It_Changed()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitAsync(_fixture.SourceWork);
        await RunAsync(_fixture.SourceWork, "update-ref", "refs/remotes/dest/main", "HEAD");
        (await EnsureAsync(_fixture.SourceNode)).Mode.Should().Be(SyncBaseMode.Git);
        await ReceiveAsync(_fixture.SourceNode, "a.txt", "sent to Dest\n");
        await ReceiveAsync(_fixture.SourceNode, "b.txt", "sent to Dest too\n");

        // Dest committed its own version of a.txt; Source pulls it.
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "Dest's version\n");
        await CommitAsync(_fixture.SourceWork, "a.txt");
        await RunAsync(_fixture.SourceWork, "update-ref", "refs/remotes/dest/main", "HEAD");
        await EnsureAsync(_fixture.SourceNode);

        SyncFixture.Read(_fixture.SourcePaths.SyncBase, "a.txt").Should().Be("Dest's version\n");
        SyncFixture.Read(_fixture.SourcePaths.SyncBase, "b.txt").Should().Be("sent to Dest too\n");
        (await MetadataAsync(_fixture.SourceNode)).Exchanged.Keys.Should().Equal("b.txt");
    }

    /// <summary>
    /// Verifies that switching to another branch starts the base from that branch's commit alone.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Another_Branch_Starts_From_Its_Commit_Alone()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        Directory.Delete(_fixture.DestPaths.SyncBase, recursive: true);
        await InitAsync(_fixture.DestWork);
        await EnsureAsync(_fixture.DestNode);
        await ReceiveAsync(_fixture.DestNode, "a.txt", "from Source\n");

        await RunAsync(_fixture.DestWork, "checkout", "--quiet", "-b", "feature");
        var status = await EnsureAsync(_fixture.DestNode);

        status.Branch.Should().Be("feature");
        SyncFixture.Read(_fixture.DestPaths.SyncBase, "a.txt").Should().Be("v1\n");
        (await MetadataAsync(_fixture.DestNode)).Exchanged.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that rebuilding the base of a Git repository starts again from the commit and forgets what was exchanged, and that
    /// it needs a commit to build from.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Rebuild_Of_A_Git_Base_Starts_Again_From_The_Commit()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        Directory.Delete(_fixture.DestPaths.SyncBase, recursive: true);
        await InitAsync(_fixture.DestWork);
        await EnsureAsync(_fixture.DestNode);
        await ReceiveAsync(_fixture.DestNode, "a.txt", "from Source\n");

        await _fixture.DestSyncBase.RebuildAsync();

        SyncFixture.Read(_fixture.DestPaths.SyncBase, "a.txt").Should().Be("v1\n");
        (await MetadataAsync(_fixture.DestNode)).Exchanged.Should().BeEmpty();

        await InitAsync(_fixture.SourceWork);
        var notPulled = () => _fixture.SourceSyncBase.RebuildAsync();
        await notPulled.Should().ThrowAsync<SyncBridgeException>("a Source repository that never pulled has no commit of Dest to build from");
    }

    /// <summary>
    /// Verifies that the staging directories a killed export left behind are deleted on the next <c>EnsureAsync</c>.
    /// </summary>
    /// <remarks>
    /// A base is built in <c>base.new-&lt;id&gt;</c> and the old one is parked in <c>base.old-&lt;id&gt;</c>; both are removed
    /// when the export finishes. A process killed mid-export runs no cleanup, so a full copy of the working tree is left on
    /// disk, once per killed start.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Leftover_Staging_Directories_Are_Deleted()
    {
        var abandonedNew = Abandon(_fixture.SourcePaths.SyncBase + ".new-" + Guid.NewGuid().ToString("N"));
        var abandonedOld = Abandon(_fixture.SourcePaths.SyncBase + ".old-" + Guid.NewGuid().ToString("N"));

        await EnsureAsync(_fixture.SourceNode);

        Directory.Exists(abandonedNew).Should().BeFalse("a staging directory of a killed export must not survive");
        Directory.Exists(abandonedOld).Should().BeFalse("a retired base of a killed export must not survive");
    }

    /// <summary>
    /// Verifies that a staging directory that is still being written is left alone, so the cleanup cannot break an export
    /// running in another process (the CLI) against the same data root.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Recent_Staging_Directories_Are_Kept()
    {
        var inFlight = _fixture.SourcePaths.SyncBase + ".new-" + Guid.NewGuid().ToString("N");
        SyncFixture.Write(inFlight, "a.txt", "being exported\n");

        await EnsureAsync(_fixture.SourceNode);

        Directory.Exists(inFlight).Should().BeTrue("an export that started moments ago may still be running");
    }

    /// <summary>
    /// Verifies that the cleanup only removes its own staging directories and leaves the base and anything else alone.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Cleanup_Keeps_The_Base_And_Unrelated_Directories()
    {
        var unrelated = Path.Combine(Path.GetDirectoryName(_fixture.SourcePaths.SyncBase)!, "keep-me");
        SyncFixture.Write(unrelated, "a.txt", "keep\n");
        await _fixture.SourceSyncBase.RebuildAsync();

        await EnsureAsync(_fixture.SourceNode);

        Directory.Exists(_fixture.SourcePaths.SyncBase).Should().BeTrue("the base itself must stay");
        SyncFixture.Read(_fixture.SourcePaths.SyncBase, "a.txt").Should().Be("v1\n");
        Directory.Exists(unrelated).Should().BeTrue("only the staging directories of the base are cleaned up");
    }

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Creates a staging directory and dates it far enough back to look like the leftover of a killed process.
    /// </summary>
    /// <param name="directory">The directory to create.</param>
    /// <returns>The same path.</returns>
    private static string Abandon(string directory)
    {
        SyncFixture.Write(directory, "big.bin", new string('x', 1024));
        var longAgo = DateTime.UtcNow - TimeSpan.FromDays(1);
        Directory.SetCreationTimeUtc(directory, longAgo);
        Directory.SetLastWriteTimeUtc(directory, longAgo);
        return directory;
    }

    /// <summary>
    /// Brings the sync base of a node up to date with its lock held, as the service does.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The status.</returns>
    private async Task<SyncBaseStatus> EnsureAsync(NodeConfig node)
    {
        using (await _fixture.LockOf(node).AcquireAsync())
        {
            return await _fixture.SyncBaseOf(node).EnsureAsync();
        }
    }

    /// <summary>
    /// Simulates an exchange with the peer: the file is written into the sync base and recorded.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="path">The relative path.</param>
    /// <param name="content">The content both nodes now have.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task ReceiveAsync(NodeConfig node, string path, string content)
    {
        var paths = node.Id == _fixture.SourceNode.Id ? _fixture.SourcePaths : _fixture.DestPaths;
        using (await _fixture.LockOf(node).AcquireAsync())
        {
            SyncFixture.Write(paths.SyncBase, path, content);
            await _fixture.SyncBaseOf(node).RecordExchangeAsync(new[] { path });
        }
    }

    /// <summary>
    /// Reads the metadata of a node's sync base.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The metadata.</returns>
    private async Task<SyncBaseMetadata> MetadataAsync(NodeConfig node)
    {
        var root = Path.Combine(_fixture.Root, node.Id == _fixture.SourceNode.Id ? "source-data" : "dest-data");
        var metadata = await new SyncBaseMetadataStore(new AppDataFactory(AppDataPaths.FromRoot(root))).ReadAsync();
        metadata.Should().NotBeNull();
        return metadata!;
    }

    /// <summary>
    /// Makes a directory a repository on <c>main</c> with one commit of its files.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task InitAsync(string directory)
    {
        await RunAsync(directory, "init", "--quiet", "--initial-branch=main");
        await RunAsync(directory, "add", "-A");
        await RunAsync(directory, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "first");
    }

    /// <summary>
    /// Gets the commit HEAD points to.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <returns>The full commit name.</returns>
    private async Task<string> FirstCommitAsync(string directory)
    {
        var result = await _git.RunAsync(new[] { "rev-parse", "HEAD" }, directory);
        return result.StandardOutput.Trim();
    }

    /// <summary>
    /// Commits one file.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <param name="path">The file.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task CommitAsync(string directory, string path)
    {
        await RunAsync(directory, "add", "--", path);
        await RunAsync(directory, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "change " + path);
    }

    /// <summary>
    /// Runs git and asserts success.
    /// </summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task RunAsync(string directory, params string[] arguments)
    {
        var result = await _git.RunAsync(arguments, directory);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)}: {result.StandardError}");
    }

    /// <summary>
    /// Checks whether git is installed.
    /// </summary>
    /// <returns><see langword="true"/> when git runs.</returns>
    private static async Task<bool> GitAvailableAsync()
    {
        try
        {
            return (await new SystemProcessRunner().RunAsync(new ProcessSpec("git", new[] { "--version" }, null, null, TimeSpan.FromSeconds(30)))).ExitCode == 0;
        }
        catch (SyncBridgeException)
        {
            return false;
        }
    }
}
