using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Commits;

/// <summary>
/// A row of the list of unsent commits on the "Synchronization" tab.
/// </summary>
public sealed partial class UnsentCommitItemViewModel : ObservableObject
{
    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Called when the user checks or clears the row.</summary>
    private readonly Action<UnsentCommitItemViewModel> _onChecked;

    /// <summary>Whether the check mark is set by the list, so the change is not taken for one of the user.</summary>
    private bool _isSetByList;

    /// <summary>Whether the commit is chosen to be sent.</summary>
    [ObservableProperty]
    private bool _isChecked;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsentCommitItemViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="commit">The commit.</param>
    /// <param name="isWaiting">Whether a delivery waits with the commit on the Origin.</param>
    /// <param name="onChecked">Called when the user checks or clears the row.</param>
    public UnsentCommitItemViewModel(ILocalizer localizer, CommitInfo commit, bool isWaiting, Action<UnsentCommitItemViewModel> onChecked)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        Commit = commit ?? throw new ArgumentNullException(nameof(commit));
        IsWaiting = isWaiting;
        _onChecked = onChecked ?? throw new ArgumentNullException(nameof(onChecked));
    }

    /// <summary>Gets the commit.</summary>
    public CommitInfo Commit { get; }

    /// <summary>Gets the full commit name.</summary>
    public string Hash => Commit.Hash;

    /// <summary>Gets the short commit name.</summary>
    public string ShortHash => Commit.ShortHash;

    /// <summary>Gets the first line of the message.</summary>
    public string Subject => Commit.Subject;

    /// <summary>Gets the author.</summary>
    public string Author => Commit.Author;

    /// <summary>Gets the author date in the language of the user.</summary>
    public string TimeText => DisplayText.Time(_localizer, Commit.AuthoredAtUtc);

    /// <summary>Gets a value indicating whether a delivery waits with the commit on the Origin.</summary>
    public bool IsWaiting { get; }

    /// <summary>
    /// Sets the check mark for the rule of the list, without taking it for a choice of the user.
    /// </summary>
    /// <param name="value">Whether the commit is chosen.</param>
    public void SetByList(bool value)
    {
        _isSetByList = true;
        try
        {
            IsChecked = value;
        }
        finally
        {
            _isSetByList = false;
        }
    }

    /// <summary>
    /// Tells the bindings that the texts of the row changed with the language.
    /// </summary>
    public void RefreshTexts() => OnPropertyChanged(nameof(TimeText));

    /// <summary>
    /// Passes a choice of the user to the list, which keeps the chosen commits contiguous.
    /// </summary>
    /// <param name="value">Whether the commit is chosen.</param>
    partial void OnIsCheckedChanged(bool value)
    {
        if (!_isSetByList)
        {
            _onChecked(this);
        }
    }
}
