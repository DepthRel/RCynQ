using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Sync.Tests.Fixtures;
using Xunit;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests for the patch receiver and the conflict resolver on real temporary working trees.
/// </summary>
public sealed class ReceiverAndResolverTests : IDisposable
{
    /// <summary>The fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ReceiverAndResolverTests"/> class with Source changes:
    /// <c>a.txt</c> modified, <c>new/n.txt</c> added, <c>b.txt</c> deleted.
    /// </summary>
    public ReceiverAndResolverTests()
    {
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");
        SyncFixture.Write(_fixture.SourceWork, "new/n.txt", "new\n");
        File.Delete(Path.Combine(_fixture.SourceWork, "b.txt"));
    }

    /// <summary>
    /// Verifies that a clean patch is stored, applied to working tree and sync base, recorded and published.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Clean_Patch_Is_Applied()
    {
        await _fixture.RegisterSourcePeerAsync();
        var envelope = await _fixture.BuildSignedEnvelopeAsync();

        var result = await _fixture.CreateReceiver().ReceiveAsync(envelope, Context());

        result.Status.Should().Be(PatchStatus.Applied);
        result.AppliedFileCount.Should().Be(3);
        foreach (var root in new[] { _fixture.DestWork, _fixture.DestPaths.SyncBase })
        {
            SyncFixture.Read(root, "a.txt").Should().Be("v2\n");
            SyncFixture.Read(root, "new/n.txt").Should().Be("new\n");
            SyncFixture.Read(root, "b.txt").Should().BeNull();
        }

        _fixture.Storage.Items.Should().ContainKey(envelope.Id);
        _fixture.Patches.Items[envelope.Id].Status.Should().Be(PatchStatus.Applied);
        await _fixture.History.Received(1).AddAsync(Arg.Is<Core.State.SyncHistoryEntry>(entry => entry.Status == PatchStatus.Applied && entry.Direction == PatchDirection.Incoming), Arg.Any<CancellationToken>());
        await _fixture.Events.Received(1).PublishPatchAsync(Arg.Is<PatchEvent>(evt => evt.PatchId == envelope.Id && evt.Status == PatchStatus.Applied), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that one local change blocks the whole patch and nothing is written.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Local_Change_Produces_Conflict_Without_Partial_Apply()
    {
        await _fixture.RegisterSourcePeerAsync();
        SyncFixture.Write(_fixture.DestWork, "a.txt", "local edit\n");
        var envelope = await _fixture.BuildSignedEnvelopeAsync();

        var result = await _fixture.CreateReceiver().ReceiveAsync(envelope, Context());

        result.Status.Should().Be(PatchStatus.ConflictPending);
        result.Conflicts.Should().ContainSingle().Which.Should().Match<ConflictFile>(conflict => conflict.Path == "a.txt" && conflict.Detail!.Contains("Modified locally"));
        SyncFixture.Read(_fixture.DestWork, "a.txt").Should().Be("local edit\n");
        SyncFixture.Read(_fixture.DestWork, "new/n.txt").Should().BeNull();
        SyncFixture.Read(_fixture.DestWork, "b.txt").Should().Be("keep\n");
        _fixture.Patches.Items[envelope.Id].Conflicts.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that a local change identical to the incoming content is not a conflict.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Identical_Local_Change_Is_Not_A_Conflict()
    {
        await _fixture.RegisterSourcePeerAsync();
        SyncFixture.Write(_fixture.DestWork, "a.txt", "v2\n");

        var result = await _fixture.CreateReceiver().ReceiveAsync(await _fixture.BuildSignedEnvelopeAsync(), Context());

        result.Status.Should().Be(PatchStatus.Applied);
    }

    /// <summary>
    /// Verifies that invalid requests are refused before anything is stored or written.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Invalid_Requests_Are_Rejected_Before_Storage()
    {
        var receiver = _fixture.CreateReceiver();
        var envelope = await _fixture.BuildSignedEnvelopeAsync();

        (await Reason(() => receiver.ReceiveAsync(envelope, Context()))).Should().Be(PatchRejectionReason.UnknownSender);
        await _fixture.RegisterSourcePeerAsync();

        (await Reason(() => receiver.ReceiveAsync(envelope, new PatchReceiveContext("source-1", "wrong-token")))).Should().Be(PatchRejectionReason.InvalidToken);
        (await Reason(() => receiver.ReceiveAsync(envelope, new PatchReceiveContext("source-1", null)))).Should().Be(PatchRejectionReason.InvalidToken);
        (await Reason(() => receiver.ReceiveAsync(envelope, new PatchReceiveContext("dest-1", SyncFixture.Token)))).Should().Be(PatchRejectionReason.SenderMismatch);

        var misdirected = await _fixture.BuildSignedEnvelopeAsync(new NodeId("other-dest"));
        (await Reason(() => receiver.ReceiveAsync(misdirected, Context()))).Should().Be(PatchRejectionReason.WrongReceiver);

        var tampered = new PatchEnvelope(envelope.Id, envelope.SourceNode, envelope.TargetNode, envelope.CreatedAtUtc.AddSeconds(1),
            envelope.BaseStateHash, envelope.IncomingStateHash, envelope.Manifest, envelope.Diff, envelope.Bundle, envelope.Signature);
        (await Reason(() => receiver.ReceiveAsync(tampered, Context()))).Should().Be(PatchRejectionReason.InvalidSignature);

        var unsigned = new PatchEnvelope(envelope.Id, envelope.SourceNode, envelope.TargetNode, envelope.CreatedAtUtc,
            envelope.BaseStateHash, envelope.IncomingStateHash, envelope.Manifest, envelope.Diff, envelope.Bundle);
        (await Reason(() => receiver.ReceiveAsync(unsigned, Context()))).Should().Be(PatchRejectionReason.InvalidSignature);

        _fixture.Storage.Items.Should().BeEmpty();
        _fixture.Patches.Items.Should().BeEmpty();
        SyncFixture.Read(_fixture.DestWork, "a.txt").Should().Be("v1\n");

        (await receiver.ReceiveAsync(envelope, Context())).Status.Should().Be(PatchStatus.Applied);
        (await Reason(() => receiver.ReceiveAsync(envelope, Context()))).Should().Be(PatchRejectionReason.Replay);
    }

    /// <summary>
    /// Verifies the KeepLocal policy.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task KeepLocal_Leaves_Local_State()
    {
        var (envelope, conflicts) = await ReceiveConflictAsync();

        var result = await _fixture.CreateResolver(Substitute.For<IGitCommandRunner>()).ResolveAsync(envelope, conflicts, ConflictPolicy.KeepLocal);

        result.Status.Should().Be(PatchStatus.Rejected);
        SyncFixture.Read(_fixture.DestWork, "a.txt").Should().Be("local edit\n");
        SyncFixture.Read(_fixture.DestWork, "new/n.txt").Should().BeNull();
    }

    /// <summary>
    /// Verifies the ApplyIncomingOverLocal policy including the backup.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ApplyIncomingOverLocal_Backs_Up_And_Overwrites()
    {
        var (envelope, conflicts) = await ReceiveConflictAsync();

        var result = await _fixture.CreateResolver(Substitute.For<IGitCommandRunner>()).ResolveAsync(envelope, conflicts, ConflictPolicy.ApplyIncomingOverLocal);

        result.Status.Should().Be(PatchStatus.ConflictResolved);
        SyncFixture.Read(_fixture.DestWork, "a.txt").Should().Be("v2\n");
        SyncFixture.Read(_fixture.DestWork, "new/n.txt").Should().Be("new\n");
        SyncFixture.Read(_fixture.DestPaths.SyncBase, "a.txt").Should().Be("v2\n");
        var backup = Directory.GetFiles(Path.Combine(_fixture.DestPaths.Backups, envelope.Id.ToString("N")), "a.txt", SearchOption.AllDirectories);
        backup.Should().ContainSingle();
        File.ReadAllText(backup[0]).Should().Be("local edit\n");
    }

    /// <summary>
    /// Verifies that UseGit outside a repository falls back to <c>--reject</c> and reports the <c>.rej</c> file.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task UseGit_Reports_Rejected_Hunks()
    {
        var (envelope, conflicts) = await ReceiveConflictAsync();
        var git = Substitute.For<IGitCommandRunner>();
        git.RunAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var args = call.Arg<IReadOnlyList<string>>();
                if (args.Contains("--reject"))
                {
                    File.WriteAllText(Path.Combine(_fixture.DestWork, "a.txt.rej"), "@@ rejected hunk @@");
                }

                return args.Contains("--is-inside-work-tree") ? new Core.Abstractions.Processes.ProcessResult(128, "", "not a git repository") : new Core.Abstractions.Processes.ProcessResult(1, "", "patch does not apply");
            });

        var result = await _fixture.CreateResolver(git).ResolveAsync(envelope, conflicts, ConflictPolicy.UseGit);

        result.Status.Should().Be(PatchStatus.ConflictPending);
        result.Conflicts.Should().ContainSingle().Which.Detail.Should().Contain("a.txt.rej");
        SyncFixture.Read(_fixture.DestPaths.SyncBase, "a.txt").Should().Be("v1\n", "the sync base changes only after a clean apply");
        await git.DidNotReceive().RunAsync(Arg.Is<IReadOnlyList<string>>(args => args.Contains("--3way")), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a clean git apply resolves the patch and updates the sync base.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task UseGit_Clean_Apply_Resolves()
    {
        var (envelope, conflicts) = await ReceiveConflictAsync();
        var git = Substitute.For<IGitCommandRunner>();
        git.RunAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyList<string>>().Contains("--is-inside-work-tree")
                ? new Core.Abstractions.Processes.ProcessResult(128, "", "")
                : new Core.Abstractions.Processes.ProcessResult(0, "", ""));

        var result = await _fixture.CreateResolver(git).ResolveAsync(envelope, conflicts, ConflictPolicy.UseGit);

        result.Status.Should().Be(PatchStatus.ConflictResolved);
        SyncFixture.Read(_fixture.DestPaths.SyncBase, "a.txt").Should().Be("v2\n");
        await git.Received(1).RunAsync(Arg.Is<IReadOnlyList<string>>(args => args.Contains("--check")), _fixture.DestWork, Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Deletes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Creates a valid request context.
    /// </summary>
    /// <returns>The context.</returns>
    private static PatchReceiveContext Context() => new("source-1", SyncFixture.Token);

    /// <summary>
    /// Receives a patch that conflicts with a local edit of <c>a.txt</c>.
    /// </summary>
    /// <returns>The envelope and its conflicts.</returns>
    private async Task<(PatchEnvelope Envelope, IReadOnlyList<ConflictFile> Conflicts)> ReceiveConflictAsync()
    {
        await _fixture.RegisterSourcePeerAsync();
        SyncFixture.Write(_fixture.DestWork, "a.txt", "local edit\n");
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var result = await _fixture.CreateReceiver().ReceiveAsync(envelope, Context());
        result.Status.Should().Be(PatchStatus.ConflictPending);
        return (envelope, result.Conflicts);
    }

    /// <summary>
    /// Runs an action and returns the rejection reason.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>The reason.</returns>
    private static async Task<PatchRejectionReason> Reason(Func<Task> action)
        => (await action.Should().ThrowAsync<PatchRejectedException>()).Which.Reason;
}
