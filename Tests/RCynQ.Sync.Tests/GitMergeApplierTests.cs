using FluentAssertions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Localization;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Backups;
using RCynQ.Sync.Tests.Fixtures;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of the three-way merge of an incoming patch into locally changed files, run with the installed Git. They
/// return early when <c>git</c> is not installed.
/// </summary>
/// <remarks>
/// The working tree of the tests is a plain directory and its files are changed without being committed anywhere:
/// that is the state a conflict is found in, and the state <c>git apply --3way</c> cannot work with.
/// </remarks>
public sealed class GitMergeApplierTests : IDisposable
{
    /// <summary>Ten lines the sides edit in different or in the same places.</summary>
    private const string BASE = "line1\nline2\nline3\nline4\nline5\nline6\nline7\nline8\nline9\nline10\n";

    /// <summary>The shared Workstation and Origin fixture; the merge runs on Origin.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The Git command runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>
    /// Initializes a new instance of the <see cref="GitMergeApplierTests"/> class: every side starts from the same text.
    /// </summary>
    public GitMergeApplierTests()
    {
        foreach (var directory in new[] { _fixture.WorkstationWork, _fixture.WorkstationBaseForBuild, _fixture.OriginWork, _fixture.OriginPaths.SyncBase })
        {
            SyncFixture.Write(directory, "text.txt", BASE);
        }
    }

    /// <summary>
    /// Verifies the reported case: the receiver changed a file locally, the sender changed another place of it. The
    /// merge keeps both, leaves no reject files behind, and the local version is backed up first.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Separate_Edits_Of_The_Same_File_Are_Both_Kept()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.OriginWork, "text.txt", BASE.Replace("line2", "line2 LOCAL"));
        SyncFixture.Write(_fixture.WorkstationWork, "text.txt", BASE.Replace("line9", "line9 INCOMING"));

        var result = await MergeAsync();

        result.Status.Should().Be(PatchStatus.Applied, result.Error);
        result.Conflicts.Should().BeEmpty();
        SyncFixture.Read(_fixture.OriginWork, "text.txt").Should().Be(BASE.Replace("line2", "line2 LOCAL").Replace("line9", "line9 INCOMING"));
        Directory.EnumerateFiles(_fixture.OriginWork, "*.rej", SearchOption.AllDirectories).Should().BeEmpty();
        Backups().Should().ContainSingle().Which.Should().EndWith("text.txt");
        File.ReadAllText(Backups().Single()).Should().Be(BASE.Replace("line2", "line2 LOCAL"));
    }

    /// <summary>
    /// Verifies that edits of the same place are left for the user between conflict markers, that the rest of the file
    /// and of the patch is merged, and that the file is named in the result.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Edits_Of_The_Same_Place_Are_Left_Between_Markers()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.OriginWork, "text.txt", BASE.Replace("line5", "line5 LOCAL"));
        SyncFixture.Write(_fixture.WorkstationWork, "text.txt", BASE.Replace("line5", "line5 INCOMING").Replace("line9", "line9 INCOMING"));
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "v2\n");

        var result = await MergeAsync();

        result.Status.Should().Be(PatchStatus.Applied, result.Error);
        var marked = result.Conflicts.Should().ContainSingle().Subject;
        marked.Path.Should().Be("text.txt");
        LocalizedText.Render(marked.Detail, TestTexts.English).Should().Contain("conflict markers");
        var merged = SyncFixture.Read(_fixture.OriginWork, "text.txt")!;
        merged.Should().Contain("<<<<<<< local\nline5 LOCAL\n=======\nline5 INCOMING\n>>>>>>> incoming\n");
        merged.Should().Contain("line9 INCOMING", "the edit that does not collide is merged");
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("v2\n", "a file the receiver did not touch takes the incoming version");
    }

    /// <summary>
    /// Verifies that the line endings of the local file win: a local file with CRLF merges with an incoming one with LF
    /// without every line colliding, and stays a CRLF file.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Local_Line_Endings_Are_Kept()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.OriginWork, "text.txt", BASE.Replace("line2", "line2 LOCAL").Replace("\n", "\r\n"));
        SyncFixture.Write(_fixture.WorkstationWork, "text.txt", BASE.Replace("line9", "line9 INCOMING"));

        var result = await MergeAsync();

        result.Status.Should().Be(PatchStatus.Applied, result.Error);
        result.Conflicts.Should().BeEmpty();

        // Read as it is on disk: the helper of the fixture hides the line endings.
        File.ReadAllText(Path.Combine(_fixture.OriginWork, "text.txt"))
            .Should().Be(BASE.Replace("line2", "line2 LOCAL").Replace("line9", "line9 INCOMING").Replace("\n", "\r\n"));
    }

    /// <summary>
    /// Verifies what happens to the files of a patch that need no merge: a file the receiver did not change takes the
    /// incoming version, a new file is created, a file the sender deleted is deleted, and a file both sides brought to
    /// the same content is left alone.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Files_That_Need_No_Merge_Follow_The_Sender()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "v2\n");
        SyncFixture.Write(_fixture.WorkstationWork, "new/added.txt", "new\n");
        File.Delete(Path.Combine(_fixture.WorkstationWork, "b.txt"));
        SyncFixture.Write(_fixture.WorkstationWork, "text.txt", BASE.Replace("line4", "line4 SAME"));
        SyncFixture.Write(_fixture.OriginWork, "text.txt", BASE.Replace("line4", "line4 SAME"));

        var result = await MergeAsync();

        result.Status.Should().Be(PatchStatus.Applied, result.Error);
        result.Conflicts.Should().BeEmpty();
        SyncFixture.Read(_fixture.OriginWork, "a.txt").Should().Be("v2\n");
        SyncFixture.Read(_fixture.OriginWork, "new/added.txt").Should().Be("new\n");
        File.Exists(Path.Combine(_fixture.OriginWork, "b.txt")).Should().BeFalse();
        SyncFixture.Read(_fixture.OriginWork, "text.txt").Should().Be(BASE.Replace("line4", "line4 SAME"));
    }

    /// <summary>
    /// Verifies that a file both sides added with different content is merged like any other: what differs is left
    /// between markers.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task File_Added_On_Both_Sides_Is_Merged()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.OriginWork, "both.txt", "local\n");
        SyncFixture.Write(_fixture.WorkstationWork, "both.txt", "incoming\n");

        var result = await MergeAsync();

        result.Status.Should().Be(PatchStatus.Applied, result.Error);
        result.Conflicts.Should().ContainSingle().Which.Path.Should().Be("both.txt");
        SyncFixture.Read(_fixture.OriginWork, "both.txt").Should().Contain("<<<<<<< local\nlocal\n").And.Contain("incoming\n>>>>>>> incoming\n");
    }

    /// <summary>
    /// Verifies that a patch with a file Git cannot merge changes nothing at all: the result names the file and the
    /// reason, the other files of the patch stay as they were, and nothing is backed up.
    /// </summary>
    /// <param name="scenario">What the two sides did to the file.</param>
    /// <param name="expected">A part of the reason.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("binary", "binary")]
    [InlineData("deleted-by-sender", "deleted")]
    [InlineData("deleted-locally", "deleted")]
    public async Task File_That_Cannot_Be_Merged_Stops_The_Whole_Merge(string scenario, string expected)
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "v2\n");
        switch (scenario)
        {
            case "binary":
                foreach (var directory in new[] { _fixture.WorkstationBaseForBuild, _fixture.OriginPaths.SyncBase })
                {
                    File.WriteAllBytes(Path.Combine(directory, "text.txt"), new byte[] { 0, 1, 2, 3 });
                }

                File.WriteAllBytes(Path.Combine(_fixture.OriginWork, "text.txt"), new byte[] { 0, 1, 2, 4 });
                File.WriteAllBytes(Path.Combine(_fixture.WorkstationWork, "text.txt"), new byte[] { 0, 1, 2, 5 });
                break;
            case "deleted-by-sender":
                SyncFixture.Write(_fixture.OriginWork, "text.txt", BASE.Replace("line2", "line2 LOCAL"));
                File.Delete(Path.Combine(_fixture.WorkstationWork, "text.txt"));
                break;
            default:
                File.Delete(Path.Combine(_fixture.OriginWork, "text.txt"));
                SyncFixture.Write(_fixture.WorkstationWork, "text.txt", BASE.Replace("line9", "line9 INCOMING"));
                break;
        }

        var before = Snapshot(_fixture.OriginWork);

        var result = await MergeAsync();

        result.Status.Should().Be(PatchStatus.ConflictPending);
        var conflict = result.Conflicts.Should().ContainSingle().Subject;
        conflict.Path.Should().Be("text.txt");
        LocalizedText.Render(conflict.Detail, TestTexts.English).Should().Contain(expected);
        Snapshot(_fixture.OriginWork).Should().Equal(before, "a merge that cannot be completed changes nothing");
        Backups().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the policy as a whole: after the merge the patch counts as resolved, the sync base holds what the sender
    /// sent, so the local edits are what the receiver has to send back, and the files with markers are reported.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Resolving_With_Git_Merges_And_Moves_The_Sync_Base()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.OriginWork, "text.txt", BASE.Replace("line5", "line5 LOCAL"));
        SyncFixture.Write(_fixture.WorkstationWork, "text.txt", BASE.Replace("line5", "line5 INCOMING"));
        var envelope = await _fixture.BuildSignedEnvelopeAsync();

        var result = await _fixture.CreateResolver(_git).ResolveAsync(envelope, _fixture.WorkstationContent, Array.Empty<ConflictFile>(), ConflictPolicy.UseGit);

        result.Status.Should().Be(PatchStatus.ConflictResolved);
        result.Conflicts.Should().ContainSingle().Which.Path.Should().Be("text.txt");
        SyncFixture.Read(_fixture.OriginWork, "text.txt").Should().Contain("<<<<<<< local");
        SyncFixture.Read(_fixture.OriginPaths.SyncBase, "text.txt").Should().Be(BASE.Replace("line5", "line5 INCOMING"));
    }

    /// <summary>
    /// Disposes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Builds the patch of the changes of Workstation and merges it into the working tree of Origin.
    /// </summary>
    /// <returns>The result of the merge.</returns>
    private async Task<PatchApplyResult> MergeAsync()
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var applier = new GitMergeApplier(
            new MergePlanner(_git, _fixture.Paths),
            new MergeOutcomeWriter(_fixture.Applier, new BackupService(_fixture.OriginPaths, _fixture.Paths, TimeProvider.System)));
        return await applier.MergeAsync(envelope, _fixture.WorkstationContent, _fixture.OriginPaths.SyncBase, _fixture.OriginWork);
    }

    /// <summary>
    /// Lists the backups made on Origin.
    /// </summary>
    /// <returns>The paths of the backed up files.</returns>
    private string[] Backups()
        => Directory.Exists(_fixture.OriginPaths.Backups) ? Directory.GetFiles(_fixture.OriginPaths.Backups, "*", SearchOption.AllDirectories) : Array.Empty<string>();

    /// <summary>
    /// Reads every file of a directory, to tell whether anything changed.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The relative paths with the content of the files, in order.</returns>
    private static string[] Snapshot(string directory)
        => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToBase64String(File.ReadAllBytes(path)))
            .ToArray();

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
        catch (RCynQ.Core.Exceptions.SyncBridgeException)
        {
            return false;
        }
    }
}
