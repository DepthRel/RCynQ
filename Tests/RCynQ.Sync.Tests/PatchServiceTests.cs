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
        await service.RebuildBaselineAsync();

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
        await service.RebuildBaselineAsync();
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
        await service.RebuildBaselineAsync();
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
        await service.RebuildBaselineAsync();
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");

        var result = await service.SendAsync();

        result.Result!.Status.Should().Be(PatchStatus.Failed);
        result.Message.Should().Contain("Permission denied");
        SyncFixture.Read(_fixture.SourcePaths.SyncBase, "a.txt").Should().Be("v1\n");
        await _fixture.StateStore.DidNotReceive().SaveAsync(Arg.Is<SyncState>(state => state.LastSentAtUtc != null), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a delivery that fails in a way nobody planned for still ends as a failed patch: the record written
    /// before the delivery must not stay "pending" for ever.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Unexpected_Delivery_Error_Is_Recorded_As_Failed()
    {
        _sender.SendAsync(Arg.Any<PatchEnvelope>(), Arg.Any<IPatchContent>(), Arg.Any<IReadOnlyList<ChainLinkConfig>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the forward is busy"));
        var service = _fixture.CreateSourceService(_sender, _chain);
        await service.RebuildBaselineAsync();
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");

        var result = await service.SendAsync();

        result.Result!.Status.Should().Be(PatchStatus.Failed);
        result.Result.Error.Should().Contain("the forward is busy");
        _fixture.Patches.Items[result.PatchId!.Value].Status.Should().Be(PatchStatus.Failed);
        SyncFixture.Read(_fixture.SourcePaths.SyncBase, "a.txt").Should().Be("v1\n");
    }

    /// <summary>
    /// Verifies that a caller who gives up right after the peer applied the patch does not stop the bookkeeping: the base
    /// is updated and the patch is recorded as applied.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Cancellation_After_Delivery_Still_Records_The_Applied_Patch()
    {
        using var caller = new CancellationTokenSource();
        _sender.SendAsync(Arg.Any<PatchEnvelope>(), Arg.Any<IPatchContent>(), Arg.Any<IReadOnlyList<ChainLinkConfig>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                caller.Cancel();
                return PatchApplyResult.Applied(call.Arg<PatchEnvelope>().Id, 1);
            });
        var service = _fixture.CreateSourceService(_sender, _chain);
        await service.RebuildBaselineAsync();
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");

        var result = await service.SendAsync(null, caller.Token);

        result.Result!.Status.Should().Be(PatchStatus.Applied);
        SyncFixture.Read(_fixture.SourcePaths.SyncBase, "a.txt").Should().Be("v2\n", "the peer has the file, so it is the base");
        _fixture.Patches.Items[result.PatchId!.Value].Status.Should().Be(PatchStatus.Applied);
    }

    /// <summary>
    /// Verifies that the base a sender reports for a changed file is the hash of its content with CRLF counted as LF: the
    /// receiver compares its own file the same way, so the line endings of either checkout never look like an edit.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Sent_Base_Hash_Ignores_The_Line_Endings_Of_The_Base()
    {
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v1\r\n");
        var service = _fixture.CreateSourceService(_sender, _chain);
        await service.RebuildBaselineAsync();
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\r\n");

        var result = await service.SendAsync();

        var sent = _fixture.Patches.Items[result.PatchId!.Value].Manifest.Single();
        sent.BaseSha256.Should().Be(RCynQ.Git.Hashing.DefaultStateHasher.Sha256Hex("v1\n"u8), "the base is reported by content, not by its bytes");
        sent.Sha256.Should().Be(RCynQ.Git.Hashing.DefaultStateHasher.Sha256Hex("v2\r\n"u8), "the content itself travels as it is");
    }

    /// <summary>
    /// Verifies resolve preconditions.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Resolve_Validates_Patch_State()
    {
        var service = _fixture.CreateSourceService(_sender, _chain);
        await service.RebuildBaselineAsync();
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");
        var sent = await service.SendAsync();

        var unknown = () => service.ResolveAsync(Guid.NewGuid(), ConflictPolicy.KeepLocal);
        var outgoing = () => service.ResolveAsync(sent.PatchId!.Value, ConflictPolicy.KeepLocal);

        await unknown.Should().ThrowAsync<KeyNotFoundException>();
        await outgoing.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Verifies that a Source working tree that is a Git repository with a resolved Dest tracking ref can send a local edit
    /// straight away, without a manual baseline, and that the delivered file is no longer offered afterwards.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Git_Source_Sends_Without_A_Manual_Baseline_And_Remembers_What_Was_Delivered()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitSourceRepositoryAsync();
        var service = _fixture.CreateSourceService(_sender, _chain);
        var ready = await service.GetStateAsync();
        ready.SyncBase.Mode.Should().Be(SyncBaseMode.Git, ready.SyncBase.Problem);
        ready.SyncBase.Branch.Should().Be("main");
        ready.PendingChanges.Should().BeEmpty();
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");

        var result = await service.SendAsync();

        result.Sent.Should().BeTrue();
        result.Result!.Status.Should().Be(PatchStatus.Applied);
        var sent = _fixture.Patches.Items[result.PatchId!.Value].Manifest.Single();
        sent.BaseSha256.Should().NotBeNull("the receiver decides conflicts by the content the sender built from");
        (await service.GetStateAsync()).PendingChanges.Should().BeEmpty("Dest already has the sent version, even before it commits it");

        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v3\n");
        var next = (await service.GetStateAsync()).PendingChanges.Should().ContainSingle().Which;
        next.Path.Should().Be("a.txt");
        next.BaseSha256.Should().Be(sent.Sha256, "the next patch builds on what Dest received, not on the commit");
    }

    /// <summary>
    /// Verifies that a delivered file leaves the pending changes but stays in the list of changed files until it is committed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Delivered_File_Stays_In_The_List_Of_Changed_Files()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitSourceRepositoryAsync();
        var service = _fixture.CreateSourceService(_sender, _chain);
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");
        (await service.GetStateAsync()).ChangedFiles.Should().Equal(new ChangedFile(ChangeKind.Modified, "a.txt", false));

        (await service.SendAsync()).Sent.Should().BeTrue();
        SyncFixture.Write(_fixture.SourceWork, "new.txt", "created\n");

        var state = await service.GetStateAsync();
        state.PendingChanges.Select(change => change.Path).Should().Equal("new.txt");
        state.ChangedFiles.Should().Equal(
            new ChangedFile(ChangeKind.Modified, "a.txt", true),
            new ChangedFile(ChangeKind.Added, "new.txt", false));
    }

    /// <summary>
    /// Verifies that without a selection every changed file is sent, also the ones the peer already has.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Send_Without_A_Selection_Sends_Delivered_Files_Again()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitSourceRepositoryAsync();
        var service = _fixture.CreateSourceService(_sender, _chain);
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");
        var first = await service.SendAsync();
        SyncFixture.Write(_fixture.SourceWork, "b.txt", "v2\n");

        var second = await service.SendAsync();

        second.Sent.Should().BeTrue();
        var manifest = _fixture.Patches.Items[second.PatchId!.Value].Manifest;
        manifest.Select(change => change.Path).Should().Equal("a.txt", "b.txt");
        manifest.Should().OnlyContain(change => change.Kind == ChangeKind.Modified && change.BaseSha256 != null);
        var again = manifest.Single(change => change.Path == "a.txt");
        again.Sha256.Should().Be(_fixture.Patches.Items[first.PatchId!.Value].Manifest.Single().Sha256, "the same version is sent once more");
        again.BaseSha256.Should().NotBeNull("the receiver decides conflicts by the content the sender built from");
    }

    /// <summary>
    /// Verifies that a selection limits the patch to the chosen files and leaves the others pending.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Send_With_A_Selection_Sends_Only_The_Chosen_Files()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitSourceRepositoryAsync();
        var service = _fixture.CreateSourceService(_sender, _chain);
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");
        SyncFixture.Write(_fixture.SourceWork, "b.txt", "v2\n");

        var result = await service.SendAsync(new[] { "b.txt", "not-a-changed-file.txt" });

        result.Sent.Should().BeTrue();
        _fixture.Patches.Items[result.PatchId!.Value].Manifest.Select(change => change.Path).Should().Equal("b.txt");
        SyncFixture.Read(_fixture.SourcePaths.SyncBase, "a.txt").Should().Be("v1\n", "the unchosen file was not delivered");
        (await service.GetStateAsync()).PendingChanges.Select(change => change.Path).Should().Equal("a.txt");
    }

    /// <summary>
    /// Verifies that a selection without any changed file sends nothing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Send_With_A_Selection_Of_Unknown_Files_Does_Nothing()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitSourceRepositoryAsync();
        var service = _fixture.CreateSourceService(_sender, _chain);
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");

        var result = await service.SendAsync(new[] { "b.txt" });

        result.Sent.Should().BeFalse();
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default);
    }

    /// <summary>
    /// Verifies that a Fetch that moves <c>refs/remotes/dest/main</c> past the working tree does not offer to send the reverse
    /// of Dest's new commit: the sync base follows the commit the working tree is built on.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Fetched_Newer_Dest_Commit_Is_Not_Offered_Back_As_A_Revert()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        var git = await InitSourceRepositoryAsync();
        var service = _fixture.CreateSourceService(_sender, _chain);
        (await service.GetStateAsync()).PendingChanges.Should().BeEmpty();

        // Dest moves on (b.txt changes); Source only fetches, so its working tree stays on the first commit.
        SyncFixture.Write(_fixture.SourceWork, "b.txt", "changed on Dest\n");
        await RunGitAsync(git, _fixture.SourceWork, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-am", "dest moves on");
        await RunGitAsync(git, _fixture.SourceWork, "update-ref", "refs/remotes/dest/main", "HEAD");
        await RunGitAsync(git, _fixture.SourceWork, "reset", "--quiet", "--hard", "HEAD~1");

        (await service.GetStateAsync()).PendingChanges.Should().BeEmpty("b.txt was changed by Dest, not by the user");
    }

    /// <summary>
    /// Regression: with <c>core.autocrlf=true</c> (the Git for Windows default) Git checks text files out with CRLF while the
    /// commit stores LF. A clean working tree must offer nothing to send, and a real edit must be the only pending file.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Clean_Checkout_With_Crlf_Conversion_Offers_Nothing()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        var git = new GitCommandRunner(new SystemProcessRunner(TestTexts.English), new GitOptions());
        await RunGitAsync(git, _fixture.SourceWork, "init", "--quiet", "--initial-branch=main");
        await RunGitAsync(git, _fixture.SourceWork, "config", "core.autocrlf", "true");
        await RunGitAsync(git, _fixture.SourceWork, "add", "-A");
        await RunGitAsync(git, _fixture.SourceWork, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "first");
        await RunGitAsync(git, _fixture.SourceWork, "update-ref", "refs/remotes/dest/main", "HEAD");
        foreach (var file in new[] { "a.txt", "b.txt" })
        {
            File.Delete(Path.Combine(_fixture.SourceWork, file));
        }

        // As the user's own Git would: with the line-ending settings of the repository.
        (await git.RunInWorkingTreeAsync(new[] { "checkout", "--", "." }, _fixture.SourceWork)).ExitCode.Should().Be(0);
        File.ReadAllText(Path.Combine(_fixture.SourceWork, "a.txt")).Should().Be("v1\r\n", "the checkout converts line endings");
        var service = _fixture.CreateSourceService(_sender, _chain);

        (await service.GetStateAsync()).PendingChanges.Should().BeEmpty("the working tree is exactly the checked out commit");
        (await service.SendAsync()).Sent.Should().BeFalse();

        // A file written with LF only is still unchanged for Git, and must stay unchanged for RCynQ too.
        SyncFixture.Write(_fixture.SourceWork, "b.txt", "keep\n");
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\r\n");

        (await service.GetStateAsync()).PendingChanges.Select(change => change.Path).Should().Equal("a.txt");
    }

    /// <summary>
    /// Verifies that rebuilding the base of a Git repository is allowed and keeps it ready.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Rebuild_Of_A_Git_Base_Keeps_It_Ready()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await InitSourceRepositoryAsync();
        var service = _fixture.CreateSourceService(_sender, _chain);
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");

        await service.RebuildBaselineAsync();

        var state = await service.GetStateAsync();
        state.SyncBase.Mode.Should().Be(SyncBaseMode.Git);
        state.PendingChanges.Select(change => change.Path).Should().Equal("a.txt");
    }

    /// <summary>
    /// Makes the Source working tree a repository with one commit that Source believes Dest has (as after a Pull).
    /// </summary>
    /// <returns>The Git runner.</returns>
    private async Task<GitCommandRunner> InitSourceRepositoryAsync()
    {
        var git = new GitCommandRunner(new SystemProcessRunner(TestTexts.English), new GitOptions());
        await RunGitAsync(git, _fixture.SourceWork, "init", "--quiet", "--initial-branch=main");
        await RunGitAsync(git, _fixture.SourceWork, "add", "-A");
        await RunGitAsync(git, _fixture.SourceWork, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "first");
        await RunGitAsync(git, _fixture.SourceWork, "update-ref", "refs/remotes/dest/main", "HEAD");
        return git;
    }

    /// <summary>
    /// Checks whether git is installed.
    /// </summary>
    /// <returns><see langword="true"/> when <c>git --version</c> works.</returns>
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
