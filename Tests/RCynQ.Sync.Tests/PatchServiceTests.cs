using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
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
        _sender.SendAsync(Arg.Any<PatchEnvelope>(), Arg.Any<IReadOnlyList<ChainLinkConfig>>(), Arg.Any<CancellationToken>())
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
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default);
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
    /// Verifies that a delivery failure is recorded and leaves the sync base untouched.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Failed_Send_Keeps_Sync_Base()
    {
        _sender.SendAsync(Arg.Any<PatchEnvelope>(), Arg.Any<IReadOnlyList<ChainLinkConfig>>(), Arg.Any<CancellationToken>())
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
    /// Deletes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();
}
