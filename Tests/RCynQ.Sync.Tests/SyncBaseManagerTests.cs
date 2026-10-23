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
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>
    /// Verifies that a Git working tree that was never pulled explains why it is not ready and does not offer a manual baseline.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workstation_Repository_Without_A_Pull_Is_Not_Ready()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitAsync(_fixture.WorkstationWork);

        var status = await EnsureAsync(_fixture.WorkstationNode);

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
        var before = await EnsureAsync(_fixture.WorkstationNode);
        before.IsReady.Should().BeFalse();
        before.CanCreateManual.Should().BeTrue();

        await _fixture.WorkstationSyncBase.RebuildAsync();
        var after = await EnsureAsync(_fixture.WorkstationNode);

        after.Mode.Should().Be(SyncBaseMode.Manual);
        SyncFixture.Read(_fixture.WorkstationPaths.SyncBase, "a.txt").Should().Be("v1\n");
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

        await InitAsync(_fixture.OriginWork);
        SyncFixture.Write(_fixture.OriginPaths.SyncBase, "a.txt", "received earlier\n");

        var status = await EnsureAsync(_fixture.OriginNode);

        status.Mode.Should().Be(SyncBaseMode.Git);
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "a.txt").Should().Be("v1\n");
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "b.txt").Should().Be("keep\n");
        (await MetadataAsync(_fixture.OriginNode)).Exchanged.Should().BeEmpty();
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

        SyncFixture.Write(_fixture.OriginWork, "c.txt", "added after the old snapshot\n");
        SyncFixture.Write(_fixture.OriginWork, "sub/d.txt", "so was this\n");
        await InitAsync(_fixture.OriginWork);
        // The old snapshot only knows a.txt, and with an outdated content.
        Directory.Delete(_fixture.OriginPaths.SyncBase, recursive: true);
        SyncFixture.Write(_fixture.OriginPaths.SyncBase, "a.txt", "very old\n");

        var tracker = _fixture.CreateTracker(_fixture.OriginNode);
        var snapshot = await tracker.CaptureAsync();
        var again = await tracker.CaptureAsync();

        snapshot.SyncBase.Mode.Should().Be(SyncBaseMode.Git);
        snapshot.PendingChanges.Should().BeEmpty("the working tree is exactly the committed state");
        again.PendingChanges.Should().BeEmpty();
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "c.txt").Should().Be("added after the old snapshot\n");
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "a.txt").Should().Be("v1\n");
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

        SyncFixture.Write(_fixture.OriginWork, "c.txt", "committed\n");
        await InitAsync(_fixture.OriginWork);
        var head = (await FirstCommitAsync(_fixture.OriginWork));
        Directory.Delete(_fixture.OriginPaths.SyncBase, recursive: true);
        SyncFixture.Write(_fixture.OriginPaths.SyncBase, "a.txt", "delivered long ago\n");
        var metadataPath = Path.Combine(_fixture.Root, "origin-data", "snapshots", "base.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        await File.WriteAllTextAsync(metadataPath, $$"""{"mode":"Git","branch":"main","commit":"{{head}}","exchanged":["a.txt","b.txt","c.txt"],"format":2}""");

        var tracker = _fixture.CreateTracker(_fixture.OriginNode);
        var snapshot = await tracker.CaptureAsync();

        snapshot.PendingChanges.Should().BeEmpty();
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "b.txt").Should().Be("keep\n");
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "c.txt").Should().Be("committed\n");
        (await MetadataAsync(_fixture.OriginNode)).Exchanged.Should().BeEmpty();
    }

    /// <summary>
    /// Regression (the Workstation of the second report): an earlier version wrote the content of a patch of Origin into the sync base of
    /// Workstation and remembered every path as exchanged, so files that Workstation's commit does not have showed up as deleted, and a
    /// changed file as modified, on a working tree that <c>git status</c> reports as clean.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workstation_With_Polluted_Base_Of_An_Earlier_Version_Has_Nothing_Pending()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitAsync(_fixture.WorkstationWork);
        await RunAsync(_fixture.WorkstationWork, "update-ref", "refs/remotes/rcynq/main", "HEAD");
        var head = await FirstCommitAsync(_fixture.WorkstationWork);
        SyncFixture.Write(_fixture.WorkstationPaths.SyncBase, "a.txt", "content of Origin, another version\n");
        SyncFixture.Write(_fixture.WorkstationPaths.SyncBase, "b.txt", "keep\n");
        SyncFixture.Write(_fixture.WorkstationPaths.SyncBase, "Portal/Jobs/Job.cs", "exists on Origin, not on this branch\n");
        var metadataPath = Path.Combine(_fixture.Root, "workstation-data", "snapshots", "base.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        await File.WriteAllTextAsync(metadataPath, $$"""{"mode":"Git","branch":"main","commit":"{{head}}","exchanged":["a.txt","b.txt","Portal/Jobs/Job.cs"],"format":2}""");

        var snapshot = await _fixture.CreateTracker(_fixture.WorkstationNode).CaptureAsync();

        snapshot.SyncBase.Mode.Should().Be(SyncBaseMode.Git);
        snapshot.PendingChanges.Should().BeEmpty("git status is clean: there is nothing the peer lacks");
        File.Exists(Path.Combine(_fixture.WorkstationPaths.SyncBase, "Portal", "Jobs", "Job.cs")).Should().BeFalse();
    }

    /// <summary>
    /// Regression: files that came from Origin in a patch and were then discarded on Workstation are still remembered as exchanged, and the
    /// sync base still holds Origin's version. <c>git status</c> is clean, so nothing may be listed as a change to send.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Discarded_Files_From_Origin_Are_Not_Listed_Because_Git_Sees_No_Change()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitAsync(_fixture.WorkstationWork);
        await RunAsync(_fixture.WorkstationWork, "update-ref", "refs/remotes/rcynq/main", "HEAD");
        var head = await FirstCommitAsync(_fixture.WorkstationWork);
        SyncFixture.Write(_fixture.WorkstationPaths.SyncBase, "a.txt", "delivered by Origin, discarded on Workstation\n");
        var metadataPath = Path.Combine(_fixture.Root, "workstation-data", "snapshots", "base.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        await File.WriteAllTextAsync(metadataPath, $$"""{"mode":"Git","branch":"main","commit":"{{head}}","exchanged":{"a.txt":null},"format":{{SyncBaseMetadata.CURRENT_FORMAT}}}""");
        var tracker = _fixture.CreateTracker(_fixture.WorkstationNode);

        var clean = await tracker.CaptureAsync();

        clean.PendingChanges.Should().BeEmpty("git status is clean");
        clean.ChangedFiles.Should().BeEmpty();

        SyncFixture.Write(_fixture.WorkstationWork, "b.txt", "edited\n");
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

        Directory.Delete(_fixture.OriginPaths.SyncBase, recursive: true);
        await InitAsync(_fixture.OriginWork);
        (await EnsureAsync(_fixture.OriginNode)).Mode.Should().Be(SyncBaseMode.Git);
        await ReceiveAsync(_fixture.OriginNode, "a.txt", "from Workstation\n");
        // Somebody (an older version, a crash, a hand edit) changes the base behind the record's back.
        SyncFixture.Write(_fixture.OriginPaths.SyncBase, "a.txt", "something else\n");

        SyncFixture.Write(_fixture.OriginWork, "b.txt", "committed on Origin\n");
        await CommitAsync(_fixture.OriginWork, "b.txt");
        await EnsureAsync(_fixture.OriginNode);

        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "a.txt").Should().Be("v1\n", "the record is not trusted, the commit is");
        (await MetadataAsync(_fixture.OriginNode)).Exchanged.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that Origin keeps a received file across its own commits, and forgets it once a commit has exactly that content.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Origin_Keeps_Exchanged_Files_Until_A_Commit_Has_Them()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        Directory.Delete(_fixture.OriginPaths.SyncBase, recursive: true);
        await InitAsync(_fixture.OriginWork);
        (await EnsureAsync(_fixture.OriginNode)).Mode.Should().Be(SyncBaseMode.Git);
        await ReceiveAsync(_fixture.OriginNode, "a.txt", "from Workstation\n");

        SyncFixture.Write(_fixture.OriginWork, "b.txt", "committed on Origin\n");
        await CommitAsync(_fixture.OriginWork, "b.txt");
        await EnsureAsync(_fixture.OriginNode);

        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "a.txt").Should().Be("from Workstation\n", "Workstation still has it and Origin did not commit it");
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "b.txt").Should().Be("committed on Origin\n");
        (await MetadataAsync(_fixture.OriginNode)).Exchanged.Keys.Should().Equal("a.txt");

        SyncFixture.Write(_fixture.OriginWork, "a.txt", "from Workstation\n");
        await CommitAsync(_fixture.OriginWork, "a.txt");
        await EnsureAsync(_fixture.OriginNode);

        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "a.txt").Should().Be("from Workstation\n");
        (await MetadataAsync(_fixture.OriginNode)).Exchanged.Should().BeEmpty("the commit has it now");
    }

    /// <summary>
    /// Verifies that when Workstation moves to a newer commit of Origin, the files that commit changed follow the commit, and the other
    /// exchanged files are kept.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workstation_Lets_A_Pulled_Commit_Replace_The_Files_It_Changed()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitAsync(_fixture.WorkstationWork);
        await RunAsync(_fixture.WorkstationWork, "update-ref", "refs/remotes/rcynq/main", "HEAD");
        (await EnsureAsync(_fixture.WorkstationNode)).Mode.Should().Be(SyncBaseMode.Git);
        await ReceiveAsync(_fixture.WorkstationNode, "a.txt", "sent to Origin\n");
        await ReceiveAsync(_fixture.WorkstationNode, "b.txt", "sent to Origin too\n");

        // Origin committed its own version of a.txt; Workstation pulls it.
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "Origin's version\n");
        await CommitAsync(_fixture.WorkstationWork, "a.txt");
        await RunAsync(_fixture.WorkstationWork, "update-ref", "refs/remotes/rcynq/main", "HEAD");
        await EnsureAsync(_fixture.WorkstationNode);

        SyncFixture.Read(_fixture.WorkstationPaths.SyncBase, "a.txt").Should().Be("Origin's version\n");
        SyncFixture.Read(_fixture.WorkstationPaths.SyncBase, "b.txt").Should().Be("sent to Origin too\n");
        (await MetadataAsync(_fixture.WorkstationNode)).Exchanged.Keys.Should().Equal("b.txt");
    }

    /// <summary>
    /// Regression: a commit made on the Workstation on top of the commit of the Origin (a cherry-pick) is delivered as a
    /// commit, so its files are not changes to send; only what is not committed is. The base still holds the version of
    /// the Origin, so a file edited again after its commit is sent against what the Origin has.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workstation_Commits_On_Top_Of_The_Origin_Are_Not_Listed_As_Files()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitAsync(_fixture.WorkstationWork);
        await RunAsync(_fixture.WorkstationWork, "update-ref", "refs/remotes/rcynq/main", "HEAD");
        SyncFixture.Write(_fixture.WorkstationWork, "b.txt", "cherry-picked\n");
        await CommitAsync(_fixture.WorkstationWork, "b.txt");
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "committed\n");
        await CommitAsync(_fixture.WorkstationWork, "a.txt");
        var tracker = _fixture.CreateTracker(_fixture.WorkstationNode);

        var clean = await tracker.CaptureAsync();
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "edited after the commit\n");
        var edited = await tracker.CaptureAsync();

        clean.PendingChanges.Should().BeEmpty("everything is committed and goes as commits");
        clean.ChangedFiles.Should().BeEmpty();
        edited.PendingChanges.Select(change => change.Path).Should().Equal("a.txt");
        edited.ChangedFiles.Select(file => file.Path).Should().Equal("a.txt");
        SyncFixture.Read(_fixture.WorkstationPaths.SyncBase, "a.txt").Should().Be("v1\n", "the base is what the Origin has");
    }

    /// <summary>
    /// Verifies that the commits of a branch that diverged from the Origin, which cannot go as commits, are still offered as files.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workstation_Commits_Of_A_Diverged_Branch_Are_Still_Listed_As_Files()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitAsync(_fixture.WorkstationWork);
        var first = await FirstCommitAsync(_fixture.WorkstationWork);
        SyncFixture.Write(_fixture.WorkstationWork, "b.txt", "local commit\n");
        await CommitAsync(_fixture.WorkstationWork, "b.txt");

        // The Origin moved on from the first commit in its own way.
        var tree = (await _git.RunAsync(new[] { "rev-parse", first + "^{tree}" }, _fixture.WorkstationWork)).StandardOutput.Trim();
        var origin = (await _git.RunAsync(new[] { "-c", "user.name=t", "-c", "user.email=t@t", "commit-tree", tree, "-p", first, "-m", "origin" }, _fixture.WorkstationWork)).StandardOutput.Trim();
        await RunAsync(_fixture.WorkstationWork, "update-ref", "refs/remotes/rcynq/main", origin);

        var snapshot = await _fixture.CreateTracker(_fixture.WorkstationNode).CaptureAsync();

        snapshot.PendingChanges.Select(change => change.Path).Should().Equal("b.txt");
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

        Directory.Delete(_fixture.OriginPaths.SyncBase, recursive: true);
        await InitAsync(_fixture.OriginWork);
        await EnsureAsync(_fixture.OriginNode);
        await ReceiveAsync(_fixture.OriginNode, "a.txt", "from Workstation\n");

        await RunAsync(_fixture.OriginWork, "checkout", "--quiet", "-b", "feature");
        var status = await EnsureAsync(_fixture.OriginNode);

        status.Branch.Should().Be("feature");
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "a.txt").Should().Be("v1\n");
        (await MetadataAsync(_fixture.OriginNode)).Exchanged.Should().BeEmpty();
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

        Directory.Delete(_fixture.OriginPaths.SyncBase, recursive: true);
        await InitAsync(_fixture.OriginWork);
        await EnsureAsync(_fixture.OriginNode);
        await ReceiveAsync(_fixture.OriginNode, "a.txt", "from Workstation\n");

        await _fixture.OriginSyncBase.RebuildAsync();

        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "a.txt").Should().Be("v1\n");
        (await MetadataAsync(_fixture.OriginNode)).Exchanged.Should().BeEmpty();

        await InitAsync(_fixture.WorkstationWork);
        var notPulled = () => _fixture.WorkstationSyncBase.RebuildAsync();
        await notPulled.Should().ThrowAsync<SyncBridgeException>("a Workstation repository that never pulled has no commit of Origin to build from");
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
        var abandonedNew = Abandon(_fixture.WorkstationPaths.SyncBase + ".new-" + Guid.NewGuid().ToString("N"));
        var abandonedOld = Abandon(_fixture.WorkstationPaths.SyncBase + ".old-" + Guid.NewGuid().ToString("N"));

        await EnsureAsync(_fixture.WorkstationNode);

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
        var inFlight = _fixture.WorkstationPaths.SyncBase + ".new-" + Guid.NewGuid().ToString("N");
        SyncFixture.Write(inFlight, "a.txt", "being exported\n");

        await EnsureAsync(_fixture.WorkstationNode);

        Directory.Exists(inFlight).Should().BeTrue("an export that started moments ago may still be running");
    }

    /// <summary>
    /// Verifies that the cleanup only removes its own staging directories and leaves the base and anything else alone.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Cleanup_Keeps_The_Base_And_Unrelated_Directories()
    {
        var unrelated = Path.Combine(Path.GetDirectoryName(_fixture.WorkstationPaths.SyncBase)!, "keep-me");
        SyncFixture.Write(unrelated, "a.txt", "keep\n");
        await _fixture.WorkstationSyncBase.RebuildAsync();

        await EnsureAsync(_fixture.WorkstationNode);

        Directory.Exists(_fixture.WorkstationPaths.SyncBase).Should().BeTrue("the base itself must stay");
        SyncFixture.Read(_fixture.WorkstationPaths.SyncBase, "a.txt").Should().Be("v1\n");
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
        var paths = node.Id == _fixture.WorkstationNode.Id ? _fixture.WorkstationPaths : _fixture.OriginPaths;
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
        var root = Path.Combine(_fixture.Root, node.Id == _fixture.WorkstationNode.Id ? "workstation-data" : "origin-data");
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
            return (await new SystemProcessRunner(TestTexts.English).RunAsync(new ProcessSpec("git", new[] { "--version" }, null, null, TimeSpan.FromSeconds(30)))).ExitCode == 0;
        }
        catch (SyncBridgeException)
        {
            return false;
        }
    }
}
