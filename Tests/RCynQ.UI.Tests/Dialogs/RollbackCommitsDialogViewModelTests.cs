using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels.Dialogs;

namespace RCynQ.UI.Tests.Dialogs;

/// <summary>
/// Tests of <see cref="RollbackCommitsDialogViewModel"/>.
/// </summary>
public sealed class RollbackCommitsDialogViewModelTests
{
    /// <summary>The commit of the Origin.</summary>
    private static readonly string _origin = new('0', 40);

    /// <summary>
    /// Verifies that the choices start from HEAD and the last one goes back to the commit of the Origin.
    /// </summary>
    [Fact]
    public void Choices_Start_From_Head_And_End_At_The_Origin()
    {
        var dialog = Create();

        dialog.Options.Select(option => option.Count).Should().Equal(1, 2, 3);
        dialog.Options.Select(option => option.Target).Should().Equal(new string('2', 40), new string('1', 40), _origin);
        dialog.SelectedOption.Should().BeSameAs(dialog.Options[0]);
        dialog.Mode.Should().Be(CommitResetMode.Soft);
    }

    /// <summary>
    /// Verifies that Hard needs the second confirmation, and that choosing it anew asks for it again.
    /// </summary>
    [Fact]
    public void Hard_Needs_A_Second_Confirmation()
    {
        var dialog = Create();

        dialog.IsSoft = false;
        dialog.IsHard = true;
        dialog.ConfirmCommand.CanExecute(null).Should().BeFalse();

        dialog.IsHardConfirmed = true;
        dialog.ConfirmCommand.CanExecute(null).Should().BeTrue();

        dialog.IsHard = false;
        dialog.IsMixed = true;
        dialog.IsHardConfirmed.Should().BeFalse();
        dialog.IsMixed = false;
        dialog.IsHard = true;
        dialog.ConfirmCommand.CanExecute(null).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that confirming records the chosen commit and mode.
    /// </summary>
    [Fact]
    public void Confirming_Records_The_Choice()
    {
        var dialog = Create();
        dialog.SelectedOption = dialog.Options[2];
        dialog.IsSoft = false;
        dialog.IsMixed = true;

        dialog.ConfirmCommand.Execute(null);

        dialog.Confirmed.Should().BeTrue();
        dialog.Result.Should().Be(new CommitRollbackChoice(_origin, CommitResetMode.Mixed));
    }

    /// <summary>
    /// Creates the dialog for three unsent commits.
    /// </summary>
    /// <returns>The dialog.</returns>
    private static RollbackCommitsDialogViewModel Create()
        => new(TestTexts.Russian, new[] { TestData.Commit('3', "three"), TestData.Commit('2', "two"), TestData.Commit('1', "one") }, _origin);
}
