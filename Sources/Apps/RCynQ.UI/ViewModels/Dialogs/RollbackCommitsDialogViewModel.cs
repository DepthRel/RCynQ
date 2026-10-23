using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Localization;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The rollback dialog: how many of the newest unsent commits are dropped and what happens to their changes.
/// </summary>
/// <remarks>
/// A rollback always starts from <c>HEAD</c>: the choices are "the newest commit", "the two newest" and so on down to all
/// unsent commits. Discarding the changes (Hard) needs a second confirmation in the dialog.
/// </remarks>
public sealed partial class RollbackCommitsDialogViewModel : DialogViewModelBase
{
    /// <summary>The chosen extent of the rollback.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private RollbackTargetOption? _selectedOption;

    /// <summary>Whether the changes stay staged.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isSoft = true;

    /// <summary>Whether the changes stay in the working tree, not staged.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isMixed;

    /// <summary>Whether the changes are discarded.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isHard;

    /// <summary>Whether the user confirmed that the changes of a Hard rollback are lost.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isHardConfirmed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RollbackCommitsDialogViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="unsent">The unsent commits, newest first.</param>
    /// <param name="originCommit">The commit the Origin has, which the rollback of every unsent commit goes back to.</param>
    public RollbackCommitsDialogViewModel(ILocalizer localizer, IReadOnlyList<CommitInfo> unsent, string originCommit)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(unsent);
        ArgumentException.ThrowIfNullOrWhiteSpace(originCommit);

        var options = new List<RollbackTargetOption>();
        for (var count = 1; count <= unsent.Count; count++)
        {
            var target = count < unsent.Count ? unsent[count] : null;
            options.Add(new RollbackTargetOption(
                count,
                target?.Hash ?? originCommit,
                target is null
                    ? localizer.Format("Commits.RollbackAll", count, originCommit[..10])
                    : localizer.Format("Commits.RollbackTo", count, target.ShortHash, target.Subject)));
        }

        Options = options;
        _selectedOption = options.FirstOrDefault();
    }

    /// <summary>Gets the extents of the rollback the user can choose from.</summary>
    public IReadOnlyList<RollbackTargetOption> Options { get; }

    /// <summary>Gets the choice, or <see langword="null"/> when the dialog was cancelled.</summary>
    public CommitRollbackChoice? Result { get; private set; }

    /// <summary>Gets the chosen mode.</summary>
    public CommitResetMode Mode => IsHard ? CommitResetMode.Hard : IsMixed ? CommitResetMode.Mixed : CommitResetMode.Soft;

    /// <summary>Gets a value indicating whether the rollback can be confirmed.</summary>
    public bool CanConfirm => SelectedOption is not null && (IsSoft || IsMixed || IsHard) && (!IsHard || IsHardConfirmed);

    /// <summary>
    /// Records the choice and closes the dialog.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = new CommitRollbackChoice(SelectedOption!.Target, Mode);
        Close(true);
    }

    /// <summary>
    /// Closes the dialog without a decision.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        Close(false);
    }

    /// <summary>
    /// Asks for the second confirmation again whenever Hard is chosen anew.
    /// </summary>
    /// <param name="value">Whether Hard is chosen.</param>
    partial void OnIsHardChanged(bool value)
    {
        if (!value)
        {
            IsHardConfirmed = false;
        }
    }
}
