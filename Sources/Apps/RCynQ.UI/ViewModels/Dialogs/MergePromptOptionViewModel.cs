using CommunityToolkit.Mvvm.Input;
using RCynQ.UI.Enums;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// One answer offered while a merge in a merge tool waits for a decision.
/// </summary>
public sealed class MergePromptOptionViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MergePromptOptionViewModel"/> class.
    /// </summary>
    /// <param name="choice">The answer.</param>
    /// <param name="text">The text of its button.</param>
    /// <param name="choose">Gives the answer.</param>
    public MergePromptOptionViewModel(MergePromptChoice choice, string text, Action<MergePromptChoice> choose)
    {
        ArgumentNullException.ThrowIfNull(choose);
        Choice = choice;
        Text = text;
        ChooseCommand = new RelayCommand(() => choose(choice));
    }

    /// <summary>Gets the answer.</summary>
    public MergePromptChoice Choice { get; }

    /// <summary>Gets the text of its button.</summary>
    public string Text { get; }

    /// <summary>Gets the command that gives the answer.</summary>
    public IRelayCommand ChooseCommand { get; }
}
