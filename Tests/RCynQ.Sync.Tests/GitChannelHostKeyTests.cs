using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.State;
using RCynQ.Git.Repository;
using RCynQ.Ssh.Commands;
using RCynQ.Ssh.Keys.HostKeys;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.Security;
using RCynQ.Sync.Tests.Fakes;
using RCynQ.Sync.Tests.Fixtures;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of the host key check before a fetch over SSH: Git connects to a host only after the host is known by its
/// pinned key.
/// </summary>
public sealed class GitChannelHostKeyTests : IDisposable
{
    /// <summary>The shared Source and Dest fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The repository service substitute; a call to it stands for a connection Git makes.</summary>
    private readonly IGitRepositoryService _git = Substitute.For<IGitRepositoryService>();

    /// <summary>The host key guard substitute.</summary>
    private readonly IPinnedHostKeyGuard _hostKeys = Substitute.For<IPinnedHostKeyGuard>();

    /// <summary>
    /// Initializes a new instance of the <see cref="GitChannelHostKeyTests"/> class.
    /// </summary>
    public GitChannelHostKeyTests()
    {
        _git.HasCommitsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _git.FetchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns("fetched");
        _git.GetBranchInfoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new GitBranchInfo(true, "main", "dest/main", 0, 0));
    }

    /// <summary>
    /// Verifies that the hosts of the route are checked before Git connects, also for the first connection of a
    /// working tree that has no commits yet.
    /// </summary>
    /// <param name="hasCommits">Whether the working tree already has commits.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Host_Keys_Are_Checked_Before_Git_Connects(bool hasCommits)
    {
        _git.HasCommitsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(hasCommits);
        _git.IsWorkTreeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var connections = new List<string>();
        _hostKeys.When(guard => guard.EnsurePinnedAsync(Arg.Any<IReadOnlyList<PeerDescriptor>>(), Arg.Any<CancellationToken>())).Do(_ => connections.Add("check"));
        _git.When(git => git.GetRemoteHeadBranchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())).Do(_ => connections.Add("ls-remote"));
        _git.When(git => git.FetchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())).Do(_ => connections.Add("fetch"));

        await CreateChannel().FetchAsync();

        connections.First().Should().Be("check");
        connections.Should().Contain("fetch");
        await _hostKeys.Received(1).EnsurePinnedAsync(
            Arg.Is<IReadOnlyList<PeerDescriptor>>(hops => hops.Select(hop => hop.Id.Value).SequenceEqual(new[] { "dest-1" })),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that Git makes no connection to a host that does not own its pinned key, and that the failure is
    /// reported in the result.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Git_Does_Not_Connect_To_A_Host_Without_Its_Pinned_Key()
    {
        _hostKeys.EnsurePinnedAsync(Arg.Any<IReadOnlyList<PeerDescriptor>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SshExecutionException("dest-1 does not own the pinned key"));

        var result = await CreateChannel().FetchAsync();

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("dest-1 does not own the pinned key");
        await _git.DidNotReceive().FetchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _git.DidNotReceive().GetRemoteHeadBranchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Disposes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Creates the Git channel of Source, with a Dest that runs an SSH server and whose host key is pinned.
    /// </summary>
    /// <returns>The channel.</returns>
    private GitChannelService CreateChannel()
    {
        var dest = new PeerDescriptor(new NodeId("dest-1"), "Dest", NodeRole.Dest, "dest.internal", 22, "rcynq", "SHA256:" + new string('C', 43), gitRepoPath: "/srv/repo");
        var links = Substitute.For<IChainLinkRepository>();
        links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { new ChainLinkConfig(Guid.NewGuid(), "dest", TransportKind.Direct, dest, 0, 18443, 100) });
        var bundles = new GitBundleClient(
            TestTexts.English,
            new PeerLinkFactory(new SocketsHttpHandler { UseProxy = false }, Substitute.For<ILocalForwardManager>()),
            _fixture.SourceNode,
            new LinkTokenProvider(new SyncBridgeOptions { LinkToken = SyncFixture.Token }, new InMemorySecretStore()));
        return new GitChannelService(
            TestTexts.English,
            _fixture.SourceNode,
            links,
            _git,
            new SshCommandBuilder(new SshClientOptions()),
            bundles,
            new AppDataFactory(AppDataPaths.FromRoot(Path.Combine(_fixture.Root, "channel-data"))),
            _fixture.SourceSyncBase,
            _fixture.SourcePaths,
            _fixture.Paths,
            _fixture.SourceLock,
            _hostKeys);
    }
}
