using FluentAssertions;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.Tests.Dialogs;

/// <summary>
/// Tests of <see cref="FileDiffDialogViewModel"/>: the rows of the diff and the check marks of its changes.
/// </summary>
public sealed class FileDiffDialogViewModelTests
{
    /// <summary>
    /// Verifies that every change is a head with its lines under it, and that the check marks give the chosen changes.
    /// </summary>
    [Fact]
    public void Rows_And_Check_Marks()
    {
        var dialog = new FileDiffDialogViewModel(TestTexts.English, Diff(canChoose: true), NodeRole.Origin);

        dialog.Rows.Should().HaveCount(6);
        dialog.Rows[0].Should().BeOfType<DiffBlockRowViewModel>().Which.Text.Should().Be(TestTexts.English.Format("Diff.Change", 1, 2, 2));
        dialog.Rows.OfType<DiffLineRowViewModel>().Select(row => row.Marker).Should().Equal(string.Empty, "-", "+", "+");
        dialog.CountText.Should().Be(TestTexts.English.Format("Diff.Count", 2, 2, NodeRole.Origin));
        dialog.HasNewChoice.Should().BeFalse();

        dialog.Blocks[1].IsSelected = false;

        dialog.ChosenBlocks.Should().Equal(0);
        dialog.CountText.Should().Be(TestTexts.English.Format("Diff.Count", 1, 2, NodeRole.Origin));
        dialog.HasNewChoice.Should().BeTrue();
        dialog.SelectNoneCommand.Execute(null);
        dialog.ChosenBlocks.Should().BeEmpty();
        dialog.SelectAllCommand.Execute(null);
        dialog.ChosenBlocks.Should().Equal(0, 1);
    }

    /// <summary>
    /// Verifies the notes: a file that cannot go in part, a binary file, and a choice that no longer fits.
    /// </summary>
    [Fact]
    public void Notices_Tell_Why()
    {
        new FileDiffDialogViewModel(TestTexts.English, Diff(canChoose: false), NodeRole.Origin).NoticeText.Should().Be(TestTexts.English.Get("Diff.ViewOnly"));
        new FileDiffDialogViewModel(TestTexts.English, Diff(canChoose: false) with { Availability = DiffAvailability.Binary, Blocks = Array.Empty<DiffBlockDto>() }, NodeRole.Origin)
            .NoticeText.Should().Be(TestTexts.English.Get("Diff.Binary"));
        var custom = new FileDiffDialogViewModel(TestTexts.English, Diff(canChoose: true) with { CustomChoice = true }, NodeRole.Origin);
        custom.NoticeText.Should().Be(TestTexts.English.Get("Diff.CustomChoice"));
        custom.HasNewChoice.Should().BeTrue("applying replaces a choice that does not fit");
        new FileDiffDialogViewModel(TestTexts.English, Diff(canChoose: true), NodeRole.Origin).HasNotice.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a file left out of the patch opens with no change checked and without the note about its earlier
    /// choice, and that only checking a change makes a new choice.
    /// </summary>
    [Fact]
    public void A_File_Left_Out_Starts_With_Nothing_Checked()
    {
        var dialog = new FileDiffDialogViewModel(TestTexts.English, Diff(canChoose: true) with { CustomChoice = true }, NodeRole.Origin, leftOut: true);

        dialog.ChosenBlocks.Should().BeEmpty();
        dialog.CountText.Should().Be(TestTexts.English.Format("Diff.Count", 0, 2, NodeRole.Origin));
        dialog.HasNotice.Should().BeFalse();
        dialog.HasNewChoice.Should().BeFalse("nothing checked keeps the file out and its earlier choice as it is");

        dialog.Blocks[1].IsSelected = true;

        dialog.HasNewChoice.Should().BeTrue();
        new FileDiffDialogViewModel(TestTexts.English, Diff(canChoose: false), NodeRole.Origin, leftOut: true).NoticeText
            .Should().Be(TestTexts.English.Get("Diff.ViewOnly"), "a file that cannot go in part is only shown");
    }

    /// <summary>
    /// Builds a diff with two changes, both chosen.
    /// </summary>
    /// <param name="canChoose">Whether a part can be chosen.</param>
    /// <returns>The diff.</returns>
    private static FileDiffResponse Diff(bool canChoose)
        => new(
            "a.txt",
            DiffAvailability.Text,
            PartialChangeState.None,
            canChoose,
            false,
            "base",
            "work",
            new[]
            {
                new DiffBlockDto(0, 1, 1, 1, 1, true, new[]
                {
                    new DiffLineDto(DiffLineKind.Context, 1, 1, "1"),
                    new DiffLineDto(DiffLineKind.Removed, 2, null, "2"),
                    new DiffLineDto(DiffLineKind.Added, null, 2, "two")
                }),
                new DiffBlockDto(1, 5, 0, 5, 1, true, new[] { new DiffLineDto(DiffLineKind.Added, null, 6, "six") })
            });
}
