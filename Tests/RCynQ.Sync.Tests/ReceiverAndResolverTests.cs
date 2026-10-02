using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Commands;
using RCynQ.Localization;
using RCynQ.Sync.Conflicts;
using RCynQ.Sync.Content;
using RCynQ.Sync.Tests.Fixtures;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests for the patch receiver and the conflict resolver on real temporary working trees.
/// </summary>
public sealed class ReceiverAndResolverTests : IDisposable
{
    /// <summary>The fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ReceiverAndResolverTests"/> class with Workstation changes:
    /// <c>a.txt</c> modified, <c>new/n.txt</c> added, <c>b.txt</c> deleted.
    /// </summary>
    public ReceiverAndResolverTests()
    {
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "v2\n");
        SyncFixture.Write(_fixture.WorkstationWork, "new/n.txt", "new\n");
        File.Delete(Path.Combine(_fixture.WorkstationWork, "b.txt"));
    }

    /// <summary>
    /// Verifies that a clean patch is applied to working tree and sync base, its content is dropped, its diff is kept, and it is recorded and published.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Clean_Patch_Is_Applied()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        var envelope = await _fixture.BuildSignedEnvelopeAsync();

        var result = await _fixture.DeliverAsync(_fixture.CreateReceiver(), envelope, Context());

        result.Status.Should().Be(PatchStatus.Applied);
        result.AppliedFileCount.Should().Be(3);
        foreach (var root in new[] { _fixture.OriginWork, _fixture.OriginPaths.SyncBase })
        {
            SyncFixture.Read(root, "a.txt").Should().Be("v2\n");
            SyncFixture.Read(root, "new/n.txt").Should().Be("new\n");
            SyncFixture.Read(root, "b.txt").Should().BeNull();
        }

        _fixture.Storage.Diffs.Should().ContainKey(envelope.Id, "the receiver builds the display diff itself");
        _fixture.Storage.Staged.Should().BeEmpty("the content of a finished patch is dropped");
        _fixture.Storage.Uploads.Should().BeEmpty();
        _fixture.Patches.Items[envelope.Id].Status.Should().Be(PatchStatus.Applied);
        await _fixture.History.Received(1).AddAsync(Arg.Is<Core.State.SyncHistoryEntry>(entry => entry.Status == PatchStatus.Applied && entry.Direction == PatchDirection.Incoming), Arg.Any<CancellationToken>());
        await _fixture.Events.Received(1).PublishPatchAsync(Arg.Is<PatchEvent>(evt => evt.PatchId == envelope.Id && evt.Status == PatchStatus.Applied), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that one local change blocks the whole patch, nothing is written, and the content is kept for the resolution.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Local_Change_Produces_Conflict_Without_Partial_Apply()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "local edit\n");
        var envelope = await _fixture.BuildSignedEnvelopeAsync();

        var result = await _fixture.DeliverAsync(_fixture.CreateReceiver(), envelope, Context());

        result.Status.Should().Be(PatchStatus.ConflictPending);
        result.Conflicts.Should().ContainSingle().Which.Should().Match<ConflictFile>(conflict => conflict.Path == "a.txt" && LocalizedText.Render(conflict.Detail, TestTexts.English)!.Contains("Modified locally"));
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("local edit\n");
        SyncFixture.Read(_fixture.OriginWork, "new/n.txt").Should().BeNull();
        SyncFixture.Read(_fixture.OriginWork, "b.txt").Should().Be("keep\n");
        _fixture.Patches.Items[envelope.Id].Conflicts.Should().ContainSingle();
        _fixture.Storage.Staged.Keys.Count(key => key.PatchId == envelope.Id).Should().Be(2, "the resolution needs the content of both files");
    }

    /// <summary>
    /// Verifies that a local change identical to the incoming content is not a conflict.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Identical_Local_Change_Is_Not_A_Conflict()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "v2\n");

        var result = await _fixture.DeliverAsync(_fixture.CreateReceiver(), await _fixture.BuildSignedEnvelopeAsync(), Context());

        result.Status.Should().Be(PatchStatus.Applied);
    }

    /// <summary>
    /// Verifies that invalid manifests are refused before anything is stored or written.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Invalid_Manifests_Are_Rejected_Before_Storage()
    {
        var receiver = _fixture.CreateReceiver();
        var envelope = await _fixture.BuildSignedEnvelopeAsync();

        (await Reason(() => receiver.OpenSessionAsync(envelope, Context()))).Should().Be(PatchRejectionReason.UnknownSender);
        await _fixture.RegisterWorkstationPeerAsync();

        (await Reason(() => receiver.OpenSessionAsync(envelope, new PatchReceiveContext("workstation-1", "wrong-token")))).Should().Be(PatchRejectionReason.InvalidToken);
        (await Reason(() => receiver.OpenSessionAsync(envelope, new PatchReceiveContext("workstation-1", null)))).Should().Be(PatchRejectionReason.InvalidToken);
        (await Reason(() => receiver.OpenSessionAsync(envelope, new PatchReceiveContext("origin-1", SyncFixture.TOKEN)))).Should().Be(PatchRejectionReason.SenderMismatch);

        var misdirected = await _fixture.BuildSignedEnvelopeAsync(new NodeId("other-origin"));
        (await Reason(() => receiver.OpenSessionAsync(misdirected, Context()))).Should().Be(PatchRejectionReason.WrongReceiver);

        var tampered = new PatchEnvelope(envelope.Id, envelope.SourceNode, envelope.TargetNode, envelope.CreatedAtUtc.AddSeconds(1),
            envelope.BaseStateHash, envelope.IncomingStateHash, envelope.Manifest, envelope.Signature);
        (await Reason(() => receiver.OpenSessionAsync(tampered, Context()))).Should().Be(PatchRejectionReason.InvalidSignature);

        var swapped = new PatchEnvelope(envelope.Id, envelope.SourceNode, envelope.TargetNode, envelope.CreatedAtUtc,
            envelope.BaseStateHash, envelope.IncomingStateHash, envelope.Manifest.Skip(1).ToArray(), envelope.Signature);
        (await Reason(() => receiver.OpenSessionAsync(swapped, Context()))).Should().Be(PatchRejectionReason.InvalidSignature, "a changed file list breaks the signature");

        var unsigned = new PatchEnvelope(envelope.Id, envelope.SourceNode, envelope.TargetNode, envelope.CreatedAtUtc,
            envelope.BaseStateHash, envelope.IncomingStateHash, envelope.Manifest);
        (await Reason(() => receiver.OpenSessionAsync(unsigned, Context()))).Should().Be(PatchRejectionReason.InvalidSignature);

        _fixture.Storage.Uploads.Should().BeEmpty();
        _fixture.Patches.Items.Should().BeEmpty();
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("v1\n");

        (await _fixture.DeliverAsync(receiver, envelope, Context())).Status.Should().Be(PatchStatus.Applied);
        (await Reason(() => receiver.OpenSessionAsync(envelope, Context()))).Should().Be(PatchRejectionReason.Replay);
    }

    /// <summary>
    /// Verifies that opening a session reports the missing content and that opening it again resumes it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Session_Reports_Missing_Content_And_Can_Be_Resumed()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        var receiver = _fixture.CreateReceiver();
        var envelope = await _fixture.BuildSignedEnvelopeAsync();

        var first = await receiver.OpenSessionAsync(envelope, Context());

        first.MissingContent.Should().BeEquivalentTo(envelope.ContentHashes);
        first.MissingContent.Should().HaveCount(2);
        _fixture.Patches.Items[envelope.Id].Status.Should().Be(PatchStatus.Pending);

        await Upload(receiver, envelope, "a.txt");
        var resumed = await receiver.OpenSessionAsync(envelope, Context());

        resumed.MissingContent.Should().ContainSingle().Which.Should().Be(HashOf("new\n"));
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("v1\n", "nothing is written before the commit");
    }

    /// <summary>
    /// Verifies that a later patch reuses content that an earlier, interrupted attempt already uploaded.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Content_Uploaded_By_An_Earlier_Attempt_Is_Not_Requested_Again()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        var receiver = _fixture.CreateReceiver();
        var interrupted = await _fixture.BuildSignedEnvelopeAsync();
        await receiver.OpenSessionAsync(interrupted, Context());
        await Upload(receiver, interrupted, "a.txt");
        await Upload(receiver, interrupted, "new/n.txt");

        var retry = await _fixture.BuildSignedEnvelopeAsync();
        var session = await receiver.OpenSessionAsync(retry, Context());

        session.MissingContent.Should().BeEmpty("the files are identical, so their hashes are already on the receiver");
        (await receiver.CommitAsync(retry.Id, Context())).Status.Should().Be(PatchStatus.Applied);
    }

    /// <summary>
    /// Verifies that content that is not part of the manifest, does not match its hash, or is larger than announced is refused and stored nowhere.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Content_Is_Verified_Against_The_Signed_Manifest()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        var receiver = _fixture.CreateReceiver();
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        await receiver.OpenSessionAsync(envelope, Context());
        var hash = HashOf("v2\n");

        var notInManifest = () => receiver.ReceiveContentAsync(envelope.Id, HashOf("surprise"), new MemoryStream("surprise"u8.ToArray()), Context());
        var wrongBytes = () => receiver.ReceiveContentAsync(envelope.Id, hash, new MemoryStream("evil\n"u8.ToArray()), Context());
        var tooLarge = () => receiver.ReceiveContentAsync(envelope.Id, hash, new MemoryStream(new byte[4096]), Context());

        (await notInManifest.Should().ThrowAsync<PatchRejectedException>()).Which.Reason.Should().Be(PatchRejectionReason.Malformed);
        (await wrongBytes.Should().ThrowAsync<PatchRejectedException>()).Which.Reason.Should().Be(PatchRejectionReason.Malformed);
        (await tooLarge.Should().ThrowAsync<PatchRejectedException>()).Which.Reason.Should().Be(PatchRejectionReason.Malformed);
        _fixture.Storage.Uploads.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that every request of a session needs the link token, the right sender and a known open session.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Session_Requests_Need_Token_Sender_And_A_Known_Session()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        var receiver = _fixture.CreateReceiver();
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var hash = HashOf("v2\n");
        var content = () => new MemoryStream("v2\n"u8.ToArray());

        (await Reason(() => receiver.ReceiveContentAsync(Guid.NewGuid(), hash, content(), Context()))).Should().Be(PatchRejectionReason.Malformed, "the session was never opened");
        await receiver.OpenSessionAsync(envelope, Context());

        (await Reason(() => receiver.ReceiveContentAsync(envelope.Id, hash, content(), new PatchReceiveContext("workstation-1", "wrong")))).Should().Be(PatchRejectionReason.InvalidToken);
        (await Reason(() => receiver.ReceiveContentAsync(envelope.Id, hash, content(), new PatchReceiveContext("origin-1", SyncFixture.TOKEN)))).Should().Be(PatchRejectionReason.SenderMismatch);
        (await Reason(() => receiver.CommitAsync(envelope.Id, new PatchReceiveContext("workstation-1", "wrong")))).Should().Be(PatchRejectionReason.InvalidToken);
        (await Reason(() => receiver.CommitAsync(envelope.Id, new PatchReceiveContext("origin-1", SyncFixture.TOKEN)))).Should().Be(PatchRejectionReason.SenderMismatch);
    }

    /// <summary>
    /// Verifies that a commit with missing content is refused and applies nothing, and that a finished session cannot be committed again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Commit_Needs_All_Content_And_Works_Only_Once()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        var receiver = _fixture.CreateReceiver();
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        await receiver.OpenSessionAsync(envelope, Context());
        await Upload(receiver, envelope, "a.txt");

        (await Reason(() => receiver.CommitAsync(envelope.Id, Context()))).Should().Be(PatchRejectionReason.Malformed);
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("v1\n");
        _fixture.Patches.Items[envelope.Id].Status.Should().Be(PatchStatus.Pending, "the session stays open for the rest of the content");

        await Upload(receiver, envelope, "new/n.txt");
        (await receiver.CommitAsync(envelope.Id, Context())).Status.Should().Be(PatchStatus.Applied);
        (await Reason(() => receiver.CommitAsync(envelope.Id, Context()))).Should().Be(PatchRejectionReason.Replay);
        (await Reason(() => receiver.ReceiveContentAsync(envelope.Id, HashOf("v2\n"), new MemoryStream("v2\n"u8.ToArray()), Context()))).Should().Be(PatchRejectionReason.Replay);
    }

    /// <summary>
    /// Verifies that a commit that has started runs to its end although the sender gives up meanwhile (its request times
    /// out and is aborted): the patch is applied completely and recorded, not left half-written and "pending".
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Commit_Runs_To_Its_End_When_The_Sender_Gives_Up()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        var receiver = _fixture.CreateReceiver();
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        await receiver.OpenSessionAsync(envelope, Context());
        await Upload(receiver, envelope, "a.txt");
        await Upload(receiver, envelope, "new/n.txt");
        using var sender = new CancellationTokenSource();

        // The display diff is built right before the files are written: the request is aborted at that moment.
        _fixture.DiffBuilder.BuildAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            sender.Cancel();
            return "diff --git a/a.txt b/a.txt\n"u8.ToArray();
        });

        var result = await receiver.CommitAsync(envelope.Id, Context(), sender.Token);

        result.Status.Should().Be(PatchStatus.Applied);
        foreach (var root in new[] { _fixture.OriginWork, _fixture.OriginPaths.SyncBase })
        {
            SyncFixture.Read(root, "a.txt").Should().Be("v2\n");
            SyncFixture.Read(root, "new/n.txt").Should().Be("new\n");
            SyncFixture.Read(root, "b.txt").Should().BeNull();
        }

        _fixture.Patches.Items[envelope.Id].Status.Should().Be(PatchStatus.Applied);
    }

    /// <summary>
    /// Verifies that a manifest that a peer signed with paths into Git metadata or outside the working tree never writes anything.
    /// </summary>
    /// <param name="hostilePath">The hostile path.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(".git/hooks/post-checkout")]
    [InlineData("../escape.txt")]
    public async Task Signed_Manifest_With_Unsafe_Path_Fails_Without_Writing(string hostilePath)
    {
        await _fixture.RegisterWorkstationPeerAsync();
        var receiver = _fixture.CreateReceiver();
        var content = "payload\n"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var unsigned = new PatchEnvelope(
            Guid.NewGuid(), new NodeId("workstation-1"), new NodeId("origin-1"), DateTimeOffset.UtcNow, hash, hash,
            new[] { new FileChange(ChangeKind.Added, hostilePath, hash, content.Length, false) });
        var envelope = unsigned.WithSignature(await _fixture.WorkstationSigner.SignAsync(Signing.PayloadCanonicalizer.Build(unsigned)));

        await receiver.OpenSessionAsync(envelope, Context());
        await receiver.ReceiveContentAsync(envelope.Id, hash, new MemoryStream(content), Context());
        var result = await receiver.CommitAsync(envelope.Id, Context());

        result.Status.Should().Be(PatchStatus.Failed);
        Directory.Exists(Path.Combine(_fixture.OriginWork, ".git")).Should().BeFalse();
        File.Exists(Path.GetFullPath(Path.Combine(_fixture.OriginWork, "..", "escape.txt"))).Should().BeFalse();
    }

    /// <summary>
    /// Verifies the KeepLocal policy.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task KeepLocal_Leaves_Local_State()
    {
        var (envelope, conflicts) = await ReceiveConflictAsync();

        var result = await _fixture.CreateResolver(Substitute.For<IGitCommandRunner>()).ResolveAsync(envelope, Stored(envelope), conflicts, ConflictPolicy.KeepLocal);

        result.Status.Should().Be(PatchStatus.Rejected);
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("local edit\n");
        SyncFixture.Read(_fixture.OriginWork, "new/n.txt").Should().BeNull();
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "a.txt").Should().Be("v2\n", "the peer keeps its version, so the base records it and the local one is offered back");
    }

    /// <summary>
    /// Verifies that the base the sender reports decides a conflict: an Origin file that equals Origin's own base but not what the
    /// sender built from was changed on Origin (for example committed there) and must not be overwritten.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Sender_Base_Decides_Even_When_The_Local_Base_Agrees_With_The_Local_File()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "committed on Origin\n");
        SyncFixture.Write(_fixture.OriginPaths.SyncBase, "a.txt", "committed on Origin\n");
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        envelope.Manifest.Single(change => change.Path == "a.txt").BaseSha256.Should().NotBeNull();

        var result = await _fixture.DeliverAsync(_fixture.CreateReceiver(), envelope, Context());

        result.Status.Should().Be(PatchStatus.ConflictPending);
        result.Conflicts.Should().ContainSingle().Which.Path.Should().Be("a.txt");
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("committed on Origin\n");
    }

    /// <summary>
    /// Verifies that an Origin file that still has exactly the content the sender built from is overwritten, even when Origin's own
    /// base holds something else.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Local_File_Equal_To_The_Sender_Base_Is_Overwritten()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        SyncFixture.Write(_fixture.OriginPaths.SyncBase, "a.txt", "stale base\n");

        var result = await _fixture.DeliverAsync(_fixture.CreateReceiver(), await _fixture.BuildSignedEnvelopeAsync(), Context());

        result.Status.Should().Be(PatchStatus.Applied);
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("v2\n");
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "a.txt").Should().Be("v2\n");
    }

    /// <summary>
    /// Verifies that a patch whose sender reports no base (an older version) is judged against the local sync base.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Patch_Without_Sender_Base_Falls_Back_To_The_Local_Base()
    {
        var detector = new ConflictDetector(_fixture.Paths);
        var incoming = new string('a', 64);
        var legacy = new[] { new FileChange(ChangeKind.Modified, "a.txt", incoming, 3, false) };
        SyncFixture.Write(_fixture.OriginPaths.SyncBase, "a.txt", "local edit\n");
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "local edit\n");

        (await detector.DetectAsync(legacy, _fixture.OriginWork, _fixture.OriginPaths.SyncBase)).Should().BeEmpty("the local file equals the local base");

        SyncFixture.Write(_fixture.OriginPaths.SyncBase, "a.txt", "v1\n");
        (await detector.DetectAsync(legacy, _fixture.OriginWork, _fixture.OriginPaths.SyncBase)).Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that the line endings of a checkout are no conflict between nodes: the sender reports its base by content
    /// (CRLF counted as LF), and the receiver compares its own file the same way. A Windows checkout with CRLF and a
    /// Linux checkout with LF of the same text therefore agree, in both directions, while a real edit is still found.
    /// </summary>
    /// <param name="local">The file of the receiver.</param>
    /// <param name="conflicts">Whether a conflict is expected.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("v1\r\n", false)]
    [InlineData("v1\n", false)]
    [InlineData("edited here\r\n", true)]
    public async Task Line_Endings_Of_A_Checkout_Are_Not_A_Conflict(string local, bool conflicts)
    {
        var detector = new ConflictDetector(_fixture.Paths);
        SyncFixture.Write(_fixture.OriginWork, "a.txt", local);

        // What a sender puts into the manifest for a base file "v1" whatever its line endings: the hash of the LF form.
        var change = new FileChange(ChangeKind.Modified, "a.txt", HashOf("v2\r\n"), 4, false, HashOf("v1\n"));

        var found = await detector.DetectAsync(new[] { change }, _fixture.OriginWork, _fixture.OriginPaths.SyncBase);

        found.Should().HaveCount(conflicts ? 1 : 0);
    }

    /// <summary>
    /// Verifies the ApplyIncomingOverLocal policy including the backup.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ApplyIncomingOverLocal_Backs_Up_And_Overwrites()
    {
        var (envelope, conflicts) = await ReceiveConflictAsync();

        var result = await _fixture.CreateResolver(Substitute.For<IGitCommandRunner>()).ResolveAsync(envelope, Stored(envelope), conflicts, ConflictPolicy.ApplyIncomingOverLocal);

        result.Status.Should().Be(PatchStatus.ConflictResolved);
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("v2\n");
        SyncFixture.Read(_fixture.OriginWork, "new/n.txt").Should().Be("new\n");
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "a.txt").Should().Be("v2\n");
        var backup = Directory.GetFiles(Path.Combine(_fixture.OriginPaths.Backups, envelope.Id.ToString("N")), "a.txt", SearchOption.AllDirectories);
        backup.Should().ContainSingle();
        File.ReadAllText(backup[0]).Should().Be("local edit\n");
    }

    /// <summary>
    /// Deletes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Creates a valid request context.
    /// </summary>
    /// <returns>The context.</returns>
    private static PatchReceiveContext Context() => new("workstation-1", SyncFixture.TOKEN);

    /// <summary>
    /// Computes the SHA-256 hash of a text as lowercase hex.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The hash.</returns>
    private static string HashOf(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>
    /// Creates the stored content of a received patch.
    /// </summary>
    /// <param name="envelope">The patch.</param>
    /// <returns>The content.</returns>
    private StoredPatchContent Stored(PatchEnvelope envelope) => new(envelope.Id, _fixture.Storage);

    /// <summary>
    /// Uploads the content of one file of the Workstation working tree to an open session.
    /// </summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="envelope">The envelope of the session.</param>
    /// <param name="path">The path of the file.</param>
    /// <returns>A task representing the upload.</returns>
    private async Task Upload(Core.Abstractions.Patches.IPatchReceiver receiver, PatchEnvelope envelope, string path)
    {
        var change = envelope.Manifest.Single(candidate => candidate.Path == path);
        await using var stream = await _fixture.WorkstationContent.OpenReadAsync(change);
        await receiver.ReceiveContentAsync(envelope.Id, change.Sha256!, stream, Context());
    }

    /// <summary>
    /// Delivers a patch that conflicts with a local edit of <c>a.txt</c>.
    /// </summary>
    /// <returns>The envelope and its conflicts.</returns>
    private async Task<(PatchEnvelope Envelope, IReadOnlyList<ConflictFile> Conflicts)> ReceiveConflictAsync()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "local edit\n");
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var result = await _fixture.DeliverAsync(_fixture.CreateReceiver(), envelope, Context());
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
