using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.Tests.Dialogs;

/// <summary>
/// Tests of the choice of the files of a patch.
/// </summary>
public sealed class ApplyPatchDialogViewModelTests
{
    /// <summary>The files of the patch of the tests.</summary>
    private static readonly ChangedFile[] _files =
    {
        new(ChangeKind.Modified, "src/a.cs", false),
        new(ChangeKind.Added, "src/b.cs", false),
        new(ChangeKind.Deleted, "readme.md", false)
    };

    /// <summary>
    /// Verifies that every file starts checked and a full choice means the whole patch.
    /// </summary>
    [Fact]
    public void Every_File_Starts_Checked()
    {
        var dialog = Create();

        dialog.Files.Should().OnlyContain(file => file.IsSelected && file.IsSelectable);
        dialog.ChosenPaths.Should().BeNull();
        dialog.CanApply.Should().BeTrue();
        dialog.CountText.Should().Contain("3");
    }

    /// <summary>
    /// Verifies that the checks of the user make the choice, and that nothing checked cannot be applied.
    /// </summary>
    [Fact]
    public void The_Choice_Follows_The_Check_Marks()
    {
        var dialog = Create();

        dialog.Files[0].IsSelected = false;
        dialog.ChosenPaths.Should().Equal("src/b.cs", "readme.md");

        dialog.ClearSelectionCommand.Execute(null);
        dialog.CanApply.Should().BeFalse();
        dialog.ApplyCommand.CanExecute(null).Should().BeFalse();

        dialog.SelectAllCommand.Execute(null);
        dialog.ApplyCommand.CanExecute(null).Should().BeTrue();
        dialog.ChosenPaths.Should().BeNull();
    }

    /// <summary>
    /// Verifies that the files are shown flat or as a tree with a working folder row, and the choice stays.
    /// </summary>
    [Fact]
    public void The_Files_Are_Shown_Flat_Or_As_A_Tree()
    {
        var dialog = Create();
        dialog.Rows.Should().HaveCount(3).And.AllBeOfType<ChangedFileItemViewModel>();

        dialog.ShowTreeCommand.Execute(null);

        var folder = dialog.Rows.OfType<ChangedFolderItemViewModel>().Single();
        folder.Path.Should().Be("src");
        folder.IsSelectable.Should().BeTrue();
        dialog.Rows.Should().HaveCount(4);

        folder.IsChecked = false;
        dialog.ChosenPaths.Should().Equal("readme.md");

        folder.ToggleCommand.Execute(null);
        dialog.Rows.Should().HaveCount(2, "the contents of a closed folder are hidden");
        dialog.ChosenPaths.Should().Equal("readme.md");
    }

    /// <summary>
    /// Verifies that the dialog starts in the layout of the Synchronization tab and keeps a new choice.
    /// </summary>
    [Fact]
    public void The_Layout_Is_The_One_Of_The_Tab()
    {
        var settings = Substitute.For<IUiSettingsStore>();
        settings.Get(ChangedFilesLayoutSetting.KEY).Returns("tree");

        var dialog = new ApplyPatchDialogViewModel(TestTexts.Russian, "changes.patch", _files, settings);
        dialog.IsTreeView.Should().BeTrue();

        dialog.ShowFlatCommand.Execute(null);
        settings.Received(1).Set(ChangedFilesLayoutSetting.KEY, "flat");
    }

    /// <summary>
    /// Verifies that the name of the patch file is on the dialog and the buttons close it with the answer of the user.
    /// </summary>
    [Fact]
    public void The_Buttons_Close_The_Dialog()
    {
        var applied = Create();
        applied.Description.Should().Contain("changes.patch");
        applied.ApplyCommand.Execute(null);
        applied.Confirmed.Should().BeTrue();

        var cancelled = Create();
        cancelled.CancelCommand.Execute(null);
        cancelled.Confirmed.Should().BeFalse();
    }

    /// <summary>
    /// Creates the dialog.
    /// </summary>
    /// <returns>The dialog.</returns>
    private static ApplyPatchDialogViewModel Create() => new(TestTexts.Russian, "changes.patch", _files);
}
