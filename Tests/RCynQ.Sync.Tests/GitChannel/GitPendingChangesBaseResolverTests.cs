using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.State;
using RCynQ.Git.Repository;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.Paths;

namespace RCynQ.Sync.Tests.GitChannel;

/// <summary>
/// Tests for <see cref="GitPendingChangesBaseResolver"/>: which comparison base a node resolves to.
/// </summary>
public sealed class GitPendingChangesBaseResolverTests : IDisposable
{
    /// <summary>The temporary root.</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rcynq-pending-base-tests", Guid.NewGuid().ToString("N"));

    /// <summary>The repository service mock.</summary>
    private readonly IGitRepositoryService _git = Substitute.For<IGitRepositoryService>();

    /// <summary>
    /// Initializes a new instance of the <see cref="GitPendingChangesBaseResolverTests"/> class.
    /// </summary>
    public GitPendingChangesBaseResolverTests() => Directory.CreateDirectory(_root);

    /// <summary>
    /// Deletes the temporary directories.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Verifies that a Dest node always falls back to the sync-base snapshot, never asking Git, even when a tracking ref
    /// would otherwise resolve.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Dest_Always_Uses_The_Snapshot_Without_Asking_Git()
    {
        var (node, paths, appData) = CreateNode(NodeRole.Dest, workdir: Path.Combine(_root, "dest-work"), dataRoot: "dest-data");
        Directory.CreateDirectory(paths.SyncBase);
        _git.GetBranchInfoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GitBranchInfo(true, "main", "refs/remotes/dest/main", 0, 0));

        var resolver = new GitPendingChangesBaseResolver(node, paths, _git, appData);
        await using var result = await resolver.ResolveAsync();

        result.Should().NotBeNull();
        result!.IsGitNative.Should().BeFalse();
        result.Directory.Should().Be(paths.SyncBase);
        await _git.DidNotReceiveWithAnyArgs().GetBranchInfoAsync(default!, default!);
    }

    /// <summary>
    /// Verifies that a Source node without a tracking ref and without a snapshot has nothing to compare against.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Source_Without_Tracking_Ref_And_Without_Baseline_Resolves_Null()
    {
        var (node, paths, appData) = CreateNode(NodeRole.Source, workdir: Path.Combine(_root, "source-work"), dataRoot: "source-data-1");
        Directory.CreateDirectory(node.EffectiveWorkdir!);
        _git.GetBranchInfoAsync(node.EffectiveWorkdir!, GitChannelService.RemoteName, Arg.Any<CancellationToken>())
            .Returns(new GitBranchInfo(true, "main", null, 0, 0));

        var resolver = new GitPendingChangesBaseResolver(node, paths, _git, appData);
        var result = await resolver.ResolveAsync();

        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a Source node without a tracking ref falls back to an existing sync-base snapshot.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Source_Without_Tracking_Ref_Falls_Back_To_An_Existing_Baseline()
    {
        var (node, paths, appData) = CreateNode(NodeRole.Source, workdir: Path.Combine(_root, "source-work"), dataRoot: "source-data-2");
        Directory.CreateDirectory(node.EffectiveWorkdir!);
        Directory.CreateDirectory(paths.SyncBase);
        _git.GetBranchInfoAsync(node.EffectiveWorkdir!, GitChannelService.RemoteName, Arg.Any<CancellationToken>())
            .Returns(new GitBranchInfo(true, "main", null, 0, 0));

        var resolver = new GitPendingChangesBaseResolver(node, paths, _git, appData);
        await using var result = await resolver.ResolveAsync();

        result.Should().NotBeNull();
        result!.IsGitNative.Should().BeFalse();
        result.Directory.Should().Be(paths.SyncBase);
    }

    /// <summary>
    /// Verifies that a Source node with a resolved tracking ref exports that ref's tree and reports the export as
    /// Git-native, that the result itself never deletes it (the resolver owns the cache), and that the sync-base snapshot
    /// is never touched.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Source_With_Tracking_Ref_Exports_The_Dest_Tree()
    {
        var (node, paths, appData) = CreateNode(NodeRole.Source, workdir: Path.Combine(_root, "source-work"), dataRoot: "source-data-3");
        Directory.CreateDirectory(node.EffectiveWorkdir!);
        Directory.CreateDirectory(paths.SyncBase);
        _git.GetBranchInfoAsync(node.EffectiveWorkdir!, GitChannelService.RemoteName, Arg.Any<CancellationToken>())
            .Returns(new GitBranchInfo(true, "main", "refs/remotes/dest/main", 1, 0));
        _git.ResolveCommitAsync(node.EffectiveWorkdir!, "refs/remotes/dest/main", Arg.Any<CancellationToken>()).Returns(new string('a', 40));

        string? exportedTo = null;
        _git.ExportTreeAsync(node.EffectiveWorkdir!, "refs/remotes/dest/main", Arg.Do<string>(dir => exportedTo = dir), Arg.Any<CancellationToken>())
            .Returns(call => Task.CompletedTask)
            .AndDoes(call => Directory.CreateDirectory(call.ArgAt<string>(2)));

        await using var resolver = new GitPendingChangesBaseResolver(node, paths, _git, appData);
        var result = await resolver.ResolveAsync();

        result.Should().NotBeNull();
        result!.IsGitNative.Should().BeTrue();
        result.Directory.Should().Be(exportedTo);
        result.Directory.Should().NotBe(paths.SyncBase);
        Directory.Exists(result.Directory).Should().BeTrue();

        await result.DisposeAsync();
        Directory.Exists(result.Directory).Should().BeTrue("the resolver owns the cached export, not the result handed out for one call");
        Directory.Exists(paths.SyncBase).Should().BeTrue("the real snapshot must never be deleted by this type");
    }

    /// <summary>
    /// Verifies that two calls against the same commit reuse the same export instead of exporting the tree twice — the
    /// point of caching, since exporting a large repository is not free.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Same_Commit_Reuses_The_Cached_Export()
    {
        var (node, paths, appData) = CreateNode(NodeRole.Source, workdir: Path.Combine(_root, "source-work"), dataRoot: "source-data-4");
        Directory.CreateDirectory(node.EffectiveWorkdir!);
        _git.GetBranchInfoAsync(node.EffectiveWorkdir!, GitChannelService.RemoteName, Arg.Any<CancellationToken>())
            .Returns(new GitBranchInfo(true, "main", "refs/remotes/dest/main", 0, 0));
        _git.ResolveCommitAsync(node.EffectiveWorkdir!, "refs/remotes/dest/main", Arg.Any<CancellationToken>()).Returns(new string('a', 40));
        _git.ExportTreeAsync(node.EffectiveWorkdir!, "refs/remotes/dest/main", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => Directory.CreateDirectory(call.ArgAt<string>(2)));

        await using var resolver = new GitPendingChangesBaseResolver(node, paths, _git, appData);
        var first = await resolver.ResolveAsync();
        var second = await resolver.ResolveAsync();

        second!.Directory.Should().Be(first!.Directory);
        await _git.Received(1).ExportTreeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a new commit on the tracking ref (a later Pull) invalidates the cache and exports again, and that
    /// disposing the resolver deletes both the current and the superseded export.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_New_Commit_On_The_Tracking_Ref_Invalidates_The_Cache()
    {
        var (node, paths, appData) = CreateNode(NodeRole.Source, workdir: Path.Combine(_root, "source-work"), dataRoot: "source-data-5");
        Directory.CreateDirectory(node.EffectiveWorkdir!);
        _git.GetBranchInfoAsync(node.EffectiveWorkdir!, GitChannelService.RemoteName, Arg.Any<CancellationToken>())
            .Returns(new GitBranchInfo(true, "main", "refs/remotes/dest/main", 0, 0));
        _git.ExportTreeAsync(node.EffectiveWorkdir!, "refs/remotes/dest/main", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => Directory.CreateDirectory(call.ArgAt<string>(2)));

        var resolver = new GitPendingChangesBaseResolver(node, paths, _git, appData);
        _git.ResolveCommitAsync(node.EffectiveWorkdir!, "refs/remotes/dest/main", Arg.Any<CancellationToken>()).Returns(new string('a', 40));
        var beforePull = await resolver.ResolveAsync();

        _git.ResolveCommitAsync(node.EffectiveWorkdir!, "refs/remotes/dest/main", Arg.Any<CancellationToken>()).Returns(new string('b', 40));
        var afterPull = await resolver.ResolveAsync();

        afterPull!.Directory.Should().NotBe(beforePull!.Directory);
        await _git.Received(2).ExportTreeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Directory.Exists(beforePull.Directory).Should().BeTrue("a caller may still be reading it; it is only removed on disposal");

        await resolver.DisposeAsync();
        Directory.Exists(beforePull.Directory).Should().BeFalse();
        Directory.Exists(afterPull.Directory).Should().BeFalse();
    }

    /// <summary>
    /// Creates a node, its sync paths and its data factory under a dedicated data root.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="workdir">The working tree.</param>
    /// <param name="dataRoot">The name of the data root directory, unique per test.</param>
    /// <returns>The node, its paths and its data factory.</returns>
    private (NodeConfig Node, SyncPaths Paths, IAppDataFactory AppData) CreateNode(NodeRole role, string workdir, string dataRoot)
    {
        var node = role == NodeRole.Source
            ? new NodeConfig(new NodeId("node-1"), "Node", role, null, workdir)
            : new NodeConfig(new NodeId("node-1"), "Node", role, workdir, null);
        var appData = new AppDataFactory(AppDataPaths.FromRoot(Path.Combine(_root, dataRoot)));
        var paths = new SyncPaths(appData, node);
        return (node, paths, appData);
    }
}
