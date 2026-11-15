using System.Text;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Sync.Partial;
using RCynQ.Sync.Services;
using RCynQ.Sync.Tests.Fixtures;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="PartialChangeService"/> and of a patch that carries only the chosen changes of a file.
/// </summary>
public sealed class PartialChangeServiceTests : IDisposable
{
    /// <summary>The file the tests change.</summary>
    private const string FILE = "c.txt";

    /// <summary>The version of the file both nodes have.</summary>
    private const string BASE = "1\n2\n3\n4\n5\n6\n7\n8\n9\n10\n";

    /// <summary>The working file: line 2 and line 9 changed.</summary>
    private const string WORK = "1\nTWO\n3\n4\n5\n6\n7\n8\nNINE\n10\n";

    /// <summary>The working file with only the change of line 2.</summary>
    private const string ONLY_FIRST = "1\nTWO\n3\n4\n5\n6\n7\n8\n9\n10\n";

    /// <summary>The fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The sender mock; it records what it streamed.</summary>
    private readonly IPatchSender _sender = Substitute.For<IPatchSender>();

    /// <summary>The chain repository mock.</summary>
    private readonly IChainLinkRepository _chain = Substitute.For<IChainLinkRepository>();

    /// <summary>What the sender streamed for the file, or <see langword="null"/>.</summary>
    private string? _streamed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartialChangeServiceTests"/> class.
    /// </summary>
    public PartialChangeServiceTests()
    {
        _chain.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_fixture.CreateChain());
        _sender.SendAsync(Arg.Any<PatchEnvelope>(), Arg.Any<IPatchContent>(), Arg.Any<IReadOnlyList<ChainLinkConfig>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var envelope = call.Arg<PatchEnvelope>();
                var change = envelope.Manifest.Single(entry => entry.Path == FILE);
                await using var stream = await call.Arg<IPatchContent>().OpenReadAsync(change);
                _streamed = await new StreamReader(stream).ReadToEndAsync();
                return PatchApplyResult.Applied(envelope.Id, envelope.Manifest.Count);
            });
    }

    /// <summary>
    /// Verifies that the diff lists every change with the lines around it, every change chosen while there is no choice.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Diff_Lists_The_Changes_With_Context()
    {
        await PrepareAsync();

        var diff = await _fixture.CreatePartialChangeService().GetDiffAsync(FILE);

        diff.Availability.Should().Be(DiffAvailability.Text);
        diff.CanChoose.Should().BeTrue();
        diff.Partial.Should().Be(PartialChangeState.None);
        diff.Blocks.Should().HaveCount(2).And.OnlyContain(block => block.Selected);
        var first = diff.Blocks[0];
        first.Lines.Select(line => (line.Kind, line.Text)).Should().Equal(
            (DiffLineKind.Context, "1"),
            (DiffLineKind.Removed, "2"),
            (DiffLineKind.Added, "TWO"),
            (DiffLineKind.Context, "3"),
            (DiffLineKind.Context, "4"),
            (DiffLineKind.Context, "5"));
        first.Lines[3].Should().Be(new DiffLineDto(DiffLineKind.Context, 3, 3, "3"));
        diff.Blocks[1].Lines.First().Text.Should().Be("6", "a line between two changes is shown once");
    }

    /// <summary>
    /// Verifies that only the chosen change goes, that the base then holds exactly it, and that the rest of the file stays
    /// pending as an ordinary change.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Only_The_Chosen_Change_Goes_And_The_Rest_Stays_Pending()
    {
        var service = await PrepareAsync();
        var partials = _fixture.CreatePartialChangeService();
        var diff = await partials.GetDiffAsync(FILE);

        var result = await partials.ChooseAsync(new ChooseChangesRequest(FILE, diff.BaseSha256, diff.WorkdirSha256, new[] { 0 }));

        result.Outcome.Should().Be(PartialChoiceOutcome.Partial);
        (await service.GetStateAsync()).ChangedFiles!.Single(file => file.Path == FILE).Partial.Should().Be(PartialChangeState.Partial);
        (await partials.GetDiffAsync(FILE)).Blocks.Select(block => block.Selected).Should().Equal(true, false);

        var sent = await service.SendAsync(new[] { FILE });

        sent.Result!.Status.Should().Be(PatchStatus.Applied);
        _streamed.Should().Be(ONLY_FIRST);
        SyncFixture.Read(_fixture.WorkstationPaths.SyncBase, FILE).Should().Be(ONLY_FIRST);
        SyncFixture.Read(_fixture.WorkstationWork, FILE).Should().Be(WORK, "the working file is never touched");
        _fixture.PartialChanges.Items.Should().BeEmpty("the peer has the choice now");
        var state = await service.GetStateAsync();
        state.PendingChanges.Should().ContainSingle().Which.Path.Should().Be(FILE);
        state.ChangedFiles!.Single(file => file.Path == FILE).Partial.Should().Be(PartialChangeState.None);
        (await partials.GetDiffAsync(FILE)).Blocks.Should().ContainSingle().Which.Lines.Should().Contain(line => line.Text == "NINE");
    }

    /// <summary>
    /// Verifies that a file edited after the choice is marked, and that the choice still goes as it was made.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task An_Edit_After_The_Choice_Keeps_The_Choice()
    {
        var service = await PrepareAsync();
        var partials = _fixture.CreatePartialChangeService();
        var diff = await partials.GetDiffAsync(FILE);
        await partials.ChooseAsync(new ChooseChangesRequest(FILE, diff.BaseSha256, diff.WorkdirSha256, new[] { 0 }));

        SyncFixture.Write(_fixture.WorkstationWork, FILE, WORK + "11\n");

        (await service.GetStateAsync()).ChangedFiles!.Single(file => file.Path == FILE).Partial.Should().Be(PartialChangeState.Outdated);
        var shown = await partials.GetDiffAsync(FILE);
        shown.Partial.Should().Be(PartialChangeState.Outdated);
        shown.Blocks.Select(block => block.Selected).Should().Equal(true, false, false);

        await service.SendAsync(new[] { FILE });

        _streamed.Should().Be(ONLY_FIRST);
    }

    /// <summary>
    /// Verifies that a choice made on another version of the peer is dropped and reported once, so it never takes back
    /// on the peer what the peer got meanwhile.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Choice_On_An_Old_Base_Is_Reset_And_Reported_Once()
    {
        var service = await PrepareAsync();
        var partials = _fixture.CreatePartialChangeService();
        var diff = await partials.GetDiffAsync(FILE);
        await partials.ChooseAsync(new ChooseChangesRequest(FILE, diff.BaseSha256, diff.WorkdirSha256, new[] { 0 }));

        SyncFixture.Write(_fixture.WorkstationPaths.SyncBase, FILE, "0\n" + BASE);

        var state = await service.GetStateAsync();
        state.ResetPartials.Should().Equal(FILE);
        state.ChangedFiles!.Single(file => file.Path == FILE).Partial.Should().Be(PartialChangeState.None);
        _fixture.PartialChanges.Items.Should().BeEmpty();
        (await service.GetStateAsync()).ResetPartials.Should().BeNull("each reset is told once");
    }

    /// <summary>
    /// Verifies that choosing every change or none keeps no choice.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Choosing_Everything_Or_Nothing_Keeps_No_Choice()
    {
        await PrepareAsync();
        var partials = _fixture.CreatePartialChangeService();
        var diff = await partials.GetDiffAsync(FILE);

        (await partials.ChooseAsync(new ChooseChangesRequest(FILE, diff.BaseSha256, diff.WorkdirSha256, new[] { 0, 1 }))).Outcome.Should().Be(PartialChoiceOutcome.Whole);
        (await partials.ChooseAsync(new ChooseChangesRequest(FILE, diff.BaseSha256, diff.WorkdirSha256, Array.Empty<int>()))).Outcome.Should().Be(PartialChoiceOutcome.Nothing);

        _fixture.PartialChanges.Items.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a choice made on a diff the file has moved away from is refused, as is a path out of the working tree.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Stale_Choices_And_Foreign_Paths_Are_Refused()
    {
        await PrepareAsync();
        var partials = _fixture.CreatePartialChangeService();
        var diff = await partials.GetDiffAsync(FILE);
        SyncFixture.Write(_fixture.WorkstationWork, FILE, WORK + "11\n");

        var stale = () => partials.ChooseAsync(new ChooseChangesRequest(FILE, diff.BaseSha256, diff.WorkdirSha256, new[] { 0 }));
        var escape = () => partials.GetDiffAsync("../outside.txt");
        var metadata = () => partials.GetDiffAsync(".git/config");

        await stale.Should().ThrowAsync<InvalidOperationException>();
        await escape.Should().ThrowAsync<ArgumentException>();
        await metadata.Should().ThrowAsync<ArgumentException>();
        _fixture.PartialChanges.Items.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the files handed to an external tool: a read-only copy of the base and the working file itself to look,
    /// an editable copy to choose; and that the version left in the tool becomes the choice.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task External_Tool_Files_And_Content()
    {
        await PrepareAsync();
        var partials = _fixture.CreatePartialChangeService();

        var view = await partials.PrepareCompareAsync(new CompareFilesRequest(FILE, ForChoice: false));
        var choice = await partials.PrepareCompareAsync(new CompareFilesRequest(FILE, ForChoice: true));

        File.ReadAllText(view.BaseFile).Should().Be(BASE);
        File.GetAttributes(view.BaseFile).Should().HaveFlag(FileAttributes.ReadOnly);
        Path.GetFileName(view.BaseFile).Should().Be(FILE);
        Path.GetFullPath(view.WorkFile).Should().Be(Path.GetFullPath(Path.Combine(_fixture.WorkstationWork, FILE)));
        File.ReadAllText(choice.WorkFile).Should().Be(WORK);
        File.GetAttributes(choice.WorkFile).Should().NotHaveFlag(FileAttributes.ReadOnly);

        var result = await partials.SetContentAsync(new SetPartialContentRequest(FILE, choice.BaseSha256, choice.WorkdirSha256, Encoding.UTF8.GetBytes(ONLY_FIRST)));

        result.Outcome.Should().Be(PartialChoiceOutcome.Partial);
        var stored = _fixture.PartialChanges.Items.Should().ContainSingle().Subject;
        stored.Blocks.Should().Equal(0);
        (await partials.PrepareCompareAsync(new CompareFilesRequest(FILE, ForChoice: true))).WorkFile.Should().Match(file => File.ReadAllText(file) == ONLY_FIRST);
        (await partials.SetContentAsync(new SetPartialContentRequest(FILE, choice.BaseSha256, choice.WorkdirSha256, Encoding.UTF8.GetBytes(WORK)))).Outcome.Should().Be(PartialChoiceOutcome.Whole);
        _fixture.PartialChanges.Items.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a binary file is shown as such and offers no choice.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Binary_Files_Offer_No_Choice()
    {
        await PrepareAsync();
        File.WriteAllBytes(Path.Combine(_fixture.WorkstationWork, "b.bin"), new byte[] { 1, 0, 2 });

        var diff = await _fixture.CreatePartialChangeService().GetDiffAsync("b.bin");

        diff.Availability.Should().Be(DiffAvailability.Binary);
        diff.CanChoose.Should().BeFalse();
        diff.Blocks.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a saved patch file carries only the chosen change.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Patch_File_Carries_Only_The_Choice()
    {
        var service = await PrepareAsync();
        var partials = _fixture.CreatePartialChangeService();
        var diff = await partials.GetDiffAsync(FILE);
        await partials.ChooseAsync(new ChooseChangesRequest(FILE, diff.BaseSha256, diff.WorkdirSha256, new[] { 1 }));

        var patch = Encoding.UTF8.GetString(await service.BuildPatchFileAsync(new[] { FILE }));

        patch.Should().Contain("+NINE").And.NotContain("+TWO");
    }

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Takes the base with the file in it and changes two of its lines in the working tree.
    /// </summary>
    /// <returns>The Workstation patch service.</returns>
    private async Task<PatchService> PrepareAsync()
    {
        var service = _fixture.CreateWorkstationService(_sender, _chain);
        SyncFixture.Write(_fixture.WorkstationWork, FILE, BASE);
        await service.RebuildBaselineAsync();
        SyncFixture.Write(_fixture.WorkstationWork, FILE, WORK);
        return service;
    }
}
