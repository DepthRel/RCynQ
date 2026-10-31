using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Localization;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Backups;
using RCynQ.Sync.History;
using RCynQ.Sync.Merging;
using RCynQ.Sync.Services;
using RCynQ.Sync.Tests.Fakes;
using RCynQ.Sync.Tests.Fixtures;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of resolving a conflicting patch in a merge tool: the files are prepared, the "tool" (the test) writes the
/// result, and the merge is applied, refused or given up. They run with the installed Git and return early without it.
/// </summary>
public sealed class MergeSessionOperationTests : IDisposable
{
    /// <summary>Ten lines the sides edit in different or in the same places.</summary>
    private const string BASE = "line1\nline2\nline3\nline4\nline5\nline6\nline7\nline8\nline9\nline10\n";

    /// <summary>The local version of the file whose edits collide.</summary>
    private const string LOCAL = "line1\nline2\nline3\nline4\nline5 LOCAL\nline6\nline7\nline8\nline9\nline10\n";

    /// <summary>The incoming version of the file whose edits collide.</summary>
    private const string INCOMING = "line1\nline2\nline3\nline4\nline5 INCOMING\nline6\nline7\nline8\nline9\nline10\n";

    /// <summary>What the user merges the colliding file to.</summary>
    private const string RESOLVED = "line1\nline2\nline3\nline4\nline5 BOTH\nline6\nline7\nline8\nline9\nline10\n";

    /// <summary>The shared Workstation and Origin fixture; the merge runs on Origin.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The Git command runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>The clock of the sessions.</summary>
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The data directories of Origin, the same ones the fixture uses.</summary>
    private readonly AppDataFactory _appData;

    /// <summary>The sessions of Origin.</summary>
    private readonly MergeSessionStore _sessions;

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeSessionOperationTests"/> class: <c>text.txt</c> is edited in
    /// the same place on both sides, <c>other.txt</c> in different places, and <c>a.txt</c> by the Workstation only.
    /// </summary>
    public MergeSessionOperationTests()
    {
        _appData = new AppDataFactory(AppDataPaths.FromRoot(Path.Combine(_fixture.Root, "origin-data")));
        _sessions = new MergeSessionStore(_appData, _clock);
        foreach (var directory in new[] { _fixture.WorkstationWork, _fixture.WorkstationBaseForBuild, _fixture.OriginWork, _fixture.OriginPaths.SyncBase })
        {
            SyncFixture.Write(directory, "text.txt", BASE);
            SyncFixture.Write(directory, "other.txt", BASE);
        }

        SyncFixture.Write(_fixture.OriginWork, "text.txt", LOCAL);
        SyncFixture.Write(_fixture.OriginWork, "other.txt", BASE.Replace("line2", "line2 LOCAL"));
        SyncFixture.Write(_fixture.WorkstationWork, "text.txt", INCOMING);
        SyncFixture.Write(_fixture.WorkstationWork, "other.txt", BASE.Replace("line9", "line9 INCOMING"));
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "v2\n");
    }

    /// <summary>
    /// Verifies the preparation: only the file whose edits collide goes to the tool, as four files named after it, with
    /// Git's merge and its markers in the result; the file Git merges on its own is counted; the working tree is untouched.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Prepare_Gives_The_Tool_Only_The_Colliding_File()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        var patchId = await DeliverConflictAsync();

        var session = await CreateOperation().PrepareAsync(patchId);

        session.SessionId.Should().NotBeNull();
        session.Unmergeable.Should().BeEmpty();
        session.AutoMergedCount.Should().Be(1, "other.txt is merged by Git");
        var file = session.Files.Should().ContainSingle().Subject;
        file.Path.Should().Be("text.txt");
        Path.GetFileName(file.LocalFile).Should().Be("text.local.txt");
        Path.GetFileName(file.BaseFile).Should().Be("text.base.txt");
        Path.GetFileName(file.IncomingFile).Should().Be("text.incoming.txt");
        Path.GetFileName(file.ResultFile).Should().Be("text.txt");
        Normalize(File.ReadAllText(file.LocalFile)).Should().Be(LOCAL);
        Normalize(File.ReadAllText(file.BaseFile)).Should().Be(BASE);
        Normalize(File.ReadAllText(file.IncomingFile)).Should().Be(INCOMING);
        Normalize(File.ReadAllText(file.ResultFile)).Should().Contain("<<<<<<< local\nline5 LOCAL\n=======\nline5 INCOMING\n>>>>>>> incoming\n");
        file.ResultFile.Should().StartWith(_appData.Create().MergeSessionsDirectory);
        SyncFixture.Read(_fixture.OriginWork, "text.txt").Should().Be(LOCAL, "nothing is written before the merge is applied");
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("v1\n");
    }

    /// <summary>
    /// Verifies the reported case end to end: what the user merged, what Git merged and what only the sender changed
    /// are written together; the local versions are backed up; the patch is resolved and recorded with the tool; the
    /// sync base takes the incoming content; the session is gone.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Complete_Writes_The_Whole_Merge()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        var patchId = await DeliverConflictAsync();
        var operation = CreateOperation();
        var session = await operation.PrepareAsync(patchId);
        await File.WriteAllTextAsync(session.Files[0].ResultFile, RESOLVED);

        var result = await operation.CompleteAsync(session.SessionId!.Value, "Visual Studio Code");

        result.Status.Should().Be(PatchStatus.ConflictResolved, result.Error);
        SyncFixture.Read(_fixture.OriginWork, "text.txt").Should().Be(RESOLVED);
        SyncFixture.Read(_fixture.OriginWork, "other.txt").Should().Be(BASE.Replace("line2", "line2 LOCAL").Replace("line9", "line9 INCOMING"));
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("v2\n");
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "text.txt").Should().Be(INCOMING, "the base holds what the peer has");
        Directory.GetFiles(_fixture.OriginPaths.Backups, "text.txt", SearchOption.AllDirectories).Should().ContainSingle();
        _fixture.Patches.Items[patchId].Status.Should().Be(PatchStatus.ConflictResolved);
        await _fixture.History.Received(1).AddAsync(
            Arg.Is<SyncHistoryEntry>(entry => entry.Status == PatchStatus.ConflictResolved && LocalizedText.Render(entry.Details, TestTexts.English)!.Contains("Visual Studio Code")),
            Arg.Any<CancellationToken>());
        _sessions.Get(session.SessionId.Value).Should().BeNull();
        Directory.Exists(Path.GetDirectoryName(Path.GetDirectoryName(session.Files[0].ResultFile))).Should().BeFalse("the files of the session are removed");
    }

    /// <summary>
    /// Verifies that a merge made against a local file that changed meanwhile is refused: nothing is written, the patch
    /// keeps waiting, and the session ends.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Complete_Refuses_A_Merge_Of_A_Local_File_That_Changed()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        var patchId = await DeliverConflictAsync();
        var operation = CreateOperation();
        var session = await operation.PrepareAsync(patchId);
        await File.WriteAllTextAsync(session.Files[0].ResultFile, RESOLVED);
        SyncFixture.Write(_fixture.OriginWork, "text.txt", LOCAL + "edited meanwhile\n");

        var complete = () => operation.CompleteAsync(session.SessionId!.Value, "KDiff3");

        (await complete.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("text.txt");
        SyncFixture.Read(_fixture.OriginWork, "text.txt").Should().Be(LOCAL + "edited meanwhile\n");
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("v1\n");
        _fixture.Patches.Items[patchId].Status.Should().Be(PatchStatus.ConflictPending);
        await complete.Should().ThrowAsync<KeyNotFoundException>("the session ended");
    }

    /// <summary>
    /// Verifies that a patch with a file no tool can merge opens no session and names the file.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Prepare_Names_Files_No_Tool_Can_Merge()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        foreach (var directory in new[] { _fixture.WorkstationBaseForBuild, _fixture.OriginPaths.SyncBase })
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "image.bin"), new byte[] { 1, 0, 2 });
        }

        await File.WriteAllBytesAsync(Path.Combine(_fixture.OriginWork, "image.bin"), new byte[] { 1, 0, 3 });
        await File.WriteAllBytesAsync(Path.Combine(_fixture.WorkstationWork, "image.bin"), new byte[] { 1, 0, 4 });
        var patchId = await DeliverConflictAsync();

        var session = await CreateOperation().PrepareAsync(patchId);

        session.SessionId.Should().BeNull();
        session.Files.Should().BeEmpty();
        session.Unmergeable.Should().ContainSingle(file => file.Path == "image.bin");
        Directory.GetDirectories(_appData.Create().MergeSessionsDirectory).Should().BeEmpty("no session was kept");
    }

    /// <summary>
    /// Verifies that giving a merge up removes the session and its files and leaves the patch waiting.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Cancel_Leaves_The_Patch_Waiting()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        var patchId = await DeliverConflictAsync();
        var operation = CreateOperation();
        var session = await operation.PrepareAsync(patchId);

        operation.Cancel(session.SessionId!.Value).Should().BeTrue();

        _sessions.Get(session.SessionId.Value).Should().BeNull();
        File.Exists(session.Files[0].ResultFile).Should().BeFalse();
        _fixture.Patches.Items[patchId].Status.Should().Be(PatchStatus.ConflictPending);
        SyncFixture.Read(_fixture.OriginWork, "text.txt").Should().Be(LOCAL);
    }

    /// <summary>
    /// Verifies that a result the tool removed is reported, and that the session stays for another try.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Complete_Without_The_Result_Keeps_The_Session()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        var patchId = await DeliverConflictAsync();
        var operation = CreateOperation();
        var session = await operation.PrepareAsync(patchId);
        File.Delete(session.Files[0].ResultFile);

        var complete = () => operation.CompleteAsync(session.SessionId!.Value, "Meld");

        await complete.Should().ThrowAsync<SyncBridgeException>();
        _sessions.Get(session.SessionId!.Value).Should().NotBeNull();
        SyncFixture.Read(_fixture.OriginWork, "text.txt").Should().Be(LOCAL);
    }

    /// <summary>
    /// Verifies that the name of the tool is checked before it goes into the history.
    /// </summary>
    /// <param name="name">A name that is refused.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tool\nname")]
    public async Task Complete_Refuses_A_Bad_Tool_Name(string name)
    {
        var complete = () => CreateOperation().CompleteAsync(Guid.NewGuid(), name);

        await complete.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Verifies that a patch decided with another policy ends its merge session.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Resolving_Otherwise_Ends_The_Session()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        var patchId = await DeliverConflictAsync();
        var session = await CreateOperation().PrepareAsync(patchId);
        var resolve = new PatchResolveOperation(
            TestTexts.English,
            _fixture.OriginNode,
            _fixture.OriginPaths,
            _fixture.CreateResolver(_git),
            new Conflicts.ConflictDetector(_fixture.Paths),
            _fixture.Patches,
            _fixture.Storage,
            Outcomes(),
            _fixture.CreateTracker(_fixture.OriginNode),
            _fixture.OriginLock,
            _fixture.OriginSyncBase,
            _sessions);

        var result = await resolve.ExecuteAsync(patchId, ConflictPolicy.KeepLocal);

        result.Status.Should().Be(PatchStatus.Rejected);
        _sessions.Get(session.SessionId!.Value).Should().BeNull();
    }

    /// <summary>
    /// Verifies the lifetime of the sessions: an old one is gone, and folders that belong to no session are removed
    /// before the next one is prepared.
    /// </summary>
    [Fact]
    public void Old_Sessions_And_Leftover_Folders_Are_Removed()
    {
        var leftover = Path.Combine(_appData.Create().MergeSessionsDirectory, "left-by-a-stopped-service");
        Directory.CreateDirectory(leftover);
        var id = Guid.NewGuid();
        var directory = _sessions.CreateDirectory(id);
        _sessions.Add(new MergeSession(id, Guid.NewGuid(), directory, _clock.GetUtcNow(), Array.Empty<MergeSessionEntry>(), Array.Empty<Core.Api.MergeSessionFile>(), 0));

        Directory.Exists(leftover).Should().BeFalse();
        _sessions.Get(id).Should().NotBeNull();

        _clock.Advance(MergeSessionStore.Lifetime + TimeSpan.FromMinutes(1));

        _sessions.Get(id).Should().BeNull();
        Directory.Exists(directory).Should().BeFalse();
    }

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Delivers the patch of the Workstation to Origin, where it waits for a decision.
    /// </summary>
    /// <returns>The identifier of the patch.</returns>
    private async Task<Guid> DeliverConflictAsync()
    {
        await _fixture.RegisterWorkstationPeerAsync();
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var result = await _fixture.DeliverAsync(_fixture.CreateReceiver(), envelope, new PatchReceiveContext("workstation-1", SyncFixture.TOKEN));
        result.Status.Should().Be(PatchStatus.ConflictPending);
        return envelope.Id;
    }

    /// <summary>
    /// Creates the operation of Origin.
    /// </summary>
    /// <returns>The operation.</returns>
    private MergeSessionOperation CreateOperation()
        => new(
            TestTexts.English,
            _fixture.OriginNode,
            _fixture.OriginPaths,
            _fixture.Paths,
            new MergePlanner(_git, _fixture.Paths),
            new MergeOutcomeWriter(_fixture.Applier, new BackupService(_fixture.OriginPaths, _fixture.Paths, TimeProvider.System)),
            _fixture.Applier,
            _fixture.Patches,
            _fixture.Storage,
            Outcomes(),
            _fixture.CreateTracker(_fixture.OriginNode),
            _fixture.OriginLock,
            _fixture.OriginSyncBase,
            _sessions,
            _clock);

    /// <summary>
    /// Creates the recorder of the outcomes of Origin.
    /// </summary>
    /// <returns>The recorder.</returns>
    private PatchOutcomeRecorder Outcomes() => new(_fixture.Patches, _fixture.History, _fixture.Events, TimeProvider.System);

    /// <summary>
    /// Brings the line endings of a text to LF, whatever the checkout used.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with LF.</returns>
    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    /// <summary>
    /// Checks whether Git is installed.
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
}
