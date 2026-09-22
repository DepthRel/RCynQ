using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Sync.Tests.Fixtures;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests for the Source side of <see cref="Services.PatchService"/>.
/// </summary>
public sealed class PatchServiceTests : IDisposable
{
    /// <summary>The fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The sender mock.</summary>
    private readonly IPatchSender _sender = Substitute.For<IPatchSender>();

    /// <summary>The chain repository mock.</summary>
    private readonly IChainLinkRepository _chain = Substitute.For<IChainLinkRepository>();

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchServiceTests"/> class.
    /// </summary>
    public PatchServiceTests()
    {
        _chain.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_fixture.CreateChain());
        _sender.SendAsync(Arg.Any<PatchEnvelope>(), Arg.Any<IPatchContent>(), Arg.Any<IReadOnlyList<ChainLinkConfig>>(), Arg.Any<CancellationToken>())
            .Returns(call => PatchApplyResult.Applied(call.Arg<PatchEnvelope>().Id, call.Arg<PatchEnvelope>().Manifest.Count));
    }

    /// <summary>
    /// Verifies that sending requires a baseline.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Send_Requires_Baseline()
    {
        var send = () => _fixture.CreateSourceService(_sender, _chain).SendAsync();

        (await send.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("baseline");
    }

    /// <summary>
    /// Verifies that no patch is sent without changes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Send_Without_Changes_Does_Nothing()
    {
        var service = _fixture.CreateSourceService(_sender, _chain);
        await service.InitializeBaselineAsync();

        var result = await service.SendAsync();

        result.Sent.Should().BeFalse();
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default);
    }

    /// <summary>
    /// Verifies that an applied patch updates the sender's sync base and is recorded.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Applied_Send_Updates_Sync_Base()
    {
        var service = _fixture.CreateSourceService(_sender, _chain);
        await service.InitializeBaselineAsync();
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");

        (await service.GetStateAsync()).PendingChanges.Should().ContainSingle().Which.Path.Should().Be("a.txt");
        var result = await service.SendAsync();

        result.Sent.Should().BeTrue();
        result.Result!.Status.Should().Be(PatchStatus.Applied);
        SyncFixture.Read(_fixture.SourcePaths.SyncBase, "a.txt").Should().Be("v2\n");
        _fixture.Patches.Items[result.PatchId!.Value].Should().Match<PatchMetadata>(patch => patch.Direction == PatchDirection.Outgoing && patch.Status == PatchStatus.Applied && patch.Signature.Length > 0);
        (await service.GetStateAsync()).PendingChanges.Should().BeEmpty();
        await _fixture.StateStore.Received().SaveAsync(Arg.Is<SyncState>(state => state.LastSentAtUtc != null), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that the sender streams from the working tree, and that a file edited while the patch is on its way stays out of
    /// the sync base so that the next patch carries the edit.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Edit_During_Send_Stays_Out_Of_The_Sync_Base()
    {
        string? streamed = null;
        _sender.SendAsync(Arg.Any<PatchEnvelope>(), Arg.Any<IPatchContent>(), Arg.Any<IReadOnlyList<ChainLinkConfig>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var envelope = call.Arg<PatchEnvelope>();
                await using (var stream = await call.Arg<IPatchContent>().OpenReadAsync(envelope.Manifest.Single()))
                {
                    streamed = await new StreamReader(stream).ReadToEndAsync();
                }

                SyncFixture.Write(_fixture.SourceWork, "a.txt", "v3, typed while sending\n");
                return PatchApplyResult.Applied(envelope.Id, 1);
            });
        var service = _fixture.CreateSourceService(_sender, _chain);
        await service.InitializeBaselineAsync();
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");

        var result = await service.SendAsync();

        result.Result!.Status.Should().Be(PatchStatus.Applied);
        streamed.Should().Be("v2\n", "the content comes from the working tree");
        SyncFixture.Read(_fixture.SourcePaths.SyncBase, "a.txt").Should().Be("v1\n", "the peer has v2 and the base must not claim more");
        (await service.GetStateAsync()).PendingChanges.Should().ContainSingle().Which.Path.Should().Be("a.txt");
        _fixture.Storage.Diffs.Should().ContainKey(result.PatchId!.Value, "the display diff is stored before the patch is sent");
    }

    /// <summary>
    /// Verifies that a delivery failure is recorded and leaves the sync base untouched.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Failed_Send_Keeps_Sync_Base()
    {
        _sender.SendAsync(Arg.Any<PatchEnvelope>(), Arg.Any<IPatchContent>(), Arg.Any<IReadOnlyList<ChainLinkConfig>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SshExecutionException("Permission denied", 255));
        var service = _fixture.CreateSourceService(_sender, _chain);
        await service.InitializeBaselineAsync();
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");

        var result = await service.SendAsync();

        result.Result!.Status.Should().Be(PatchStatus.Failed);
        result.Message.Should().Contain("Permission denied");
        SyncFixture.Read(_fixture.SourcePaths.SyncBase, "a.txt").Should().Be("v1\n");
    }

    /// <summary>
    /// Verifies resolve preconditions.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Resolve_Validates_Patch_State()
    {
        var service = _fixture.CreateSourceService(_sender, _chain);
        await service.InitializeBaselineAsync();
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");
        var sent = await service.SendAsync();

        var unknown = () => service.ResolveAsync(Guid.NewGuid(), ConflictPolicy.KeepLocal);
        var outgoing = () => service.ResolveAsync(sent.PatchId!.Value, ConflictPolicy.KeepLocal);

        await unknown.Should().ThrowAsync<KeyNotFoundException>();
        await outgoing.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Verifies that a Source working tree that is a Git repository with a resolved Dest tracking ref can send a local edit
    /// straight away, without ever creating the sync-base snapshot baseline — the point of the Git-native redesign.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Send_On_A_Git_Native_Source_Never_Creates_A_Baseline()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        var git = new GitCommandRunner(new SystemProcessRunner(), new GitOptions());
        await RunGitAsync(git, _fixture.SourceWork, "init", "--quiet", "--initial-branch=main");
        await RunGitAsync(git, _fixture.SourceWork, "add", "-A");
        await RunGitAsync(git, _fixture.SourceWork, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "first");

        // Simulates a prior pull: Source already knows this exact commit as "what Dest has".
        await RunGitAsync(git, _fixture.SourceWork, "update-ref", "refs/remotes/dest/main", "HEAD");

        var service = _fixture.CreateSourceService(_sender, _chain);
        (await service.GetStateAsync()).BaselineInitialized.Should().BeTrue("a resolved Dest tracking ref is enough to be ready to send");
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");

        var result = await service.SendAsync();

        result.Sent.Should().BeTrue();
        result.Result!.Status.Should().Be(PatchStatus.Applied);
        Directory.Exists(_fixture.SourcePaths.SyncBase).Should().BeFalse("Git truth is the comparison base; no snapshot is ever written");
    }

    /// <summary>
    /// Checks whether git is installed.
    /// </summary>
    /// <returns><see langword="true"/> when <c>git --version</c> works.</returns>
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

    /// <summary>
    /// Runs git and asserts success.
    /// </summary>
    /// <param name="git">The git runner.</param>
    /// <param name="directory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>A task representing the operation.</returns>
    private static async Task RunGitAsync(IGitCommandRunner git, string directory, params string[] arguments)
    {
        var result = await git.RunAsync(arguments, directory);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)}: {result.StandardError}");
    }

    /// <summary>
    /// Deletes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();
}
