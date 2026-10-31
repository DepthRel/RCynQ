using FluentAssertions;
using RCynQ.Core.Patches;
using RCynQ.MergeTools;
using RCynQ.UI.ViewModels.Dialogs;

namespace RCynQ.UI.Tests.Dialogs;

/// <summary>
/// Tests of the choice of a merge tool in the conflict dialog.
/// </summary>
public sealed class ConflictDialogMergeToolTests
{
    /// <summary>VS Code found on the machine.</summary>
    private static readonly MergeTool _code = new("vscode", "Visual Studio Code", @"C:\Code\Code.exe", new[] { "{local}", "{incoming}", "{result}" }, false);

    /// <summary>KDiff3 found on the machine.</summary>
    private static readonly MergeTool _kdiff3 = new("kdiff3", "KDiff3", @"C:\KDiff3\kdiff3.exe", new[] { "{base}", "{local}", "{incoming}", "-o", "{result}" }, true);

    /// <summary>
    /// Verifies that the tool of the settings is offered first, another one can be picked for this conflict, and the
    /// choice closes the dialog with the tool.
    /// </summary>
    [Fact]
    public void The_Picked_Tool_Closes_The_Dialog()
    {
        var dialog = Dialog(new[] { _code, _kdiff3 }, _kdiff3);
        bool? closed = null;
        dialog.CloseRequested += (_, confirmed) => closed = confirmed;

        dialog.SelectedTool.Should().Be(_kdiff3);
        dialog.HasToolChoice.Should().BeTrue();
        dialog.MergeToolText.Should().Contain("KDiff3");

        dialog.SelectedTool = _code;
        dialog.UseMergeToolCommand.Execute(null);

        dialog.Result.Should().Be(new ConflictChoice(null, _code));
        closed.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a tool the user described, which the search does not find, is offered too.
    /// </summary>
    [Fact]
    public void A_Described_Tool_Is_Offered()
    {
        var custom = new MergeTool(MergeTool.CUSTOM_ID, "merge", @"D:\merge.exe", new[] { "{local}", "{incoming}", "{result}" }, false);

        var dialog = Dialog(new[] { _code }, custom);

        dialog.Tools.Should().Equal(custom, _code);
        dialog.SelectedTool.Should().Be(custom);
    }

    /// <summary>
    /// Verifies that without a tool the choice cannot be made and says where to get one.
    /// </summary>
    [Fact]
    public void Without_A_Tool_The_Choice_Is_Off()
    {
        var dialog = Dialog(Array.Empty<MergeTool>(), null);

        dialog.HasMergeTool.Should().BeFalse();
        dialog.HasToolChoice.Should().BeFalse();
        dialog.UseMergeToolCommand.CanExecute(null).Should().BeFalse();
        dialog.MergeToolText.Should().Be(TestTexts.Russian.Get("Conflict.ExternalToolNone"));
    }

    /// <summary>
    /// Creates the dialog of a conflict in one file.
    /// </summary>
    /// <param name="tools">The tools of the machine.</param>
    /// <param name="defaultTool">The tool of the settings.</param>
    /// <returns>The dialog.</returns>
    private static ConflictDialogViewModel Dialog(IReadOnlyList<MergeTool> tools, MergeTool? defaultTool)
        => new(TestTexts.Russian, Guid.NewGuid(), "workstation-1", new[] { new ConflictFile("src/app.cs", null, null) }, tools, defaultTool);
}
