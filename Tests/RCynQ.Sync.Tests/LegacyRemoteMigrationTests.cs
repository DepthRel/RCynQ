using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Git.Repository;
using RCynQ.Sync.GitChannel;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of the rename of the remote-tracking namespace an earlier version left in a working copy.
/// </summary>
public sealed class LegacyRemoteMigrationTests
{
    /// <summary>The working copy.</summary>
    private const string WORKDIR = "/work/app";

    /// <summary>The repository service substitute.</summary>
    private readonly IGitRepositoryService _git = Substitute.For<IGitRepositoryService>();

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacyRemoteMigrationTests"/> class.
    /// </summary>
    public LegacyRemoteMigrationTests()
    {
        _git.IsWorkTreeAsync(WORKDIR, Arg.Any<CancellationToken>()).Returns(true);
    }

    /// <summary>
    /// Verifies that the working copy of a Workstation is renamed from the earlier namespace to the current one, once.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workstation_Working_Copy_Is_Renamed_Once()
    {
        var migration = new LegacyRemoteMigration(Node(NodeRole.Workstation), _git);

        await migration.EnsureAsync(WORKDIR);
        await migration.EnsureAsync(WORKDIR);

        await _git.Received(1).RenameRemoteAsync(WORKDIR, "dest", GitChannelService.REMOTE_NAME, Arg.Any<string>(), Arg.Any<CancellationToken>());
        GitChannelService.REMOTE_NAME.Should().Be("rcynq");
    }

    /// <summary>
    /// Verifies that the repository of an Origin is never looked at, let alone written to.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Origin_Repository_Is_Never_Touched()
    {
        var migration = new LegacyRemoteMigration(Node(NodeRole.Origin), _git);

        await migration.EnsureAsync(WORKDIR);

        _git.ReceivedCalls().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a folder that is not a repository yet is looked at again later.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Folder_Without_A_Repository_Is_Looked_At_Again()
    {
        _git.IsWorkTreeAsync(WORKDIR, Arg.Any<CancellationToken>()).Returns(false, true);
        var migration = new LegacyRemoteMigration(Node(NodeRole.Workstation), _git);

        await migration.EnsureAsync(WORKDIR);
        await _git.DidNotReceiveWithAnyArgs().RenameRemoteAsync(default!, default!, default!, default!);

        await migration.EnsureAsync(WORKDIR);
        await _git.Received(1).RenameRemoteAsync(WORKDIR, "dest", "rcynq", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a failure of Git does not break the caller and that the rename is tried again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Git_Failure_Is_Not_Fatal_And_Is_Retried()
    {
        _git.RenameRemoteAsync(WORKDIR, "dest", "rcynq", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new GitExecutionException("git remote rename failed"), _ => true);
        var migration = new LegacyRemoteMigration(Node(NodeRole.Workstation), _git);

        var first = () => migration.EnsureAsync(WORKDIR);
        await first.Should().NotThrowAsync();
        await migration.EnsureAsync(WORKDIR);

        await _git.Received(2).RenameRemoteAsync(WORKDIR, "dest", "rcynq", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Creates a node of a role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The node.</returns>
    private static NodeConfig Node(NodeRole role)
        => new(new NodeId("node-1"), "Node", role, role == NodeRole.Origin ? WORKDIR : null, role == NodeRole.Workstation ? WORKDIR : null);
}
