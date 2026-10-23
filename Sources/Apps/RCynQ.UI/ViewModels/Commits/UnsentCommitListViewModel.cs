using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Commits;

/// <summary>
/// The list of the commits of the current branch the Origin does not have yet, and which of them are chosen to be sent.
/// </summary>
/// <remarks>
/// The rows are shown newest first, as <c>git log</c> shows them. The chosen commits always form one block that starts
/// with the oldest unsent commit, since the Origin can only take commits that follow what it has: checking a commit checks
/// every older one, clearing a commit clears every newer one.
/// </remarks>
public sealed partial class UnsentCommitListViewModel : ObservableObject
{
    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The last answer of the service, or <see langword="null"/> before the first one.</summary>
    private UnsentCommitsResponse? _response;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsentCommitListViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    public UnsentCommitListViewModel(ILocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <summary>Gets the unsent commits, newest first.</summary>
    public ObservableCollection<UnsentCommitItemViewModel> Items { get; } = new();

    /// <summary>Gets a value indicating whether the area is shown: there are local commits, listed or not.</summary>
    public bool IsVisible => Items.Count > 0 || _response?.State is UnsentCommitsState.Diverged or UnsentCommitsState.TooMany;

    /// <summary>Gets why the commits cannot be sent, or <see langword="null"/>.</summary>
    public string? Problem => _response?.State == UnsentCommitsState.Ready ? null : _response?.Problem;

    /// <summary>Gets a value indicating whether a problem is shown.</summary>
    public bool HasProblem => IsVisible && Problem is not null;

    /// <summary>Gets the branch the commits belong to, or <see langword="null"/>.</summary>
    public string? Branch => _response?.Branch;

    /// <summary>Gets the commit the Origin has on top of the branch, or <see langword="null"/>.</summary>
    public string? OriginCommit => _response?.OriginCommit;

    /// <summary>Gets the commit checked out, or <see langword="null"/>.</summary>
    public string? HeadCommit => _response?.HeadCommit;

    /// <summary>Gets the number of chosen commits.</summary>
    public int SelectedCount => Items.Count(item => item.IsChecked);

    /// <summary>Gets the newest chosen commit, the last one to send, or <see langword="null"/>.</summary>
    public string? SelectedTip => Items.FirstOrDefault(item => item.IsChecked)?.Hash;

    /// <summary>Gets a value indicating whether the chosen commits can be sent.</summary>
    public bool CanSend => SelectedCount > 0 && Problem is null;

    /// <summary>Gets a value indicating whether unsent commits can be rolled back.</summary>
    public bool CanRollBack => Items.Count > 0 && Problem is null;

    /// <summary>Gets the number of commits next to the title of the area.</summary>
    public string CountText => Items.Count == 0
        ? string.Empty
        : $"({(SelectedCount > 0 ? _localizer.Format("Sync.SelectedOf", SelectedCount, Items.Count) : DisplayText.Count(_localizer, "Commits.Count", Items.Count))})";

    /// <summary>
    /// Shows a new answer of the service, keeping the commits the user chose that are still unsent.
    /// </summary>
    /// <param name="response">The answer.</param>
    public void Load(UnsentCommitsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var chosen = Items.Where(item => item.IsChecked).Select(item => item.Hash).ToHashSet(StringComparer.Ordinal);
        var waiting = response.WaitingCommits.ToHashSet(StringComparer.Ordinal);
        _response = response;

        Items.Clear();
        foreach (var commit in response.Commits.Reverse())
        {
            Items.Add(new UnsentCommitItemViewModel(_localizer, commit, waiting.Contains(commit.Hash), OnChecked));
        }

        // The newest commit still chosen decides the block; the rule fills in what it covers.
        var newest = Items.FirstOrDefault(item => chosen.Contains(item.Hash));
        if (newest is not null)
        {
            Choose(Items.IndexOf(newest), true);
        }

        Changed();
    }

    /// <summary>
    /// Forgets which commits were chosen.
    /// </summary>
    public void ClearSelection()
    {
        foreach (var item in Items)
        {
            item.SetByList(false);
        }

        Changed();
    }

    /// <summary>
    /// Tells the bindings that the texts changed with the language.
    /// </summary>
    public void RefreshTexts()
    {
        foreach (var item in Items)
        {
            item.RefreshTexts();
        }

        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>
    /// Keeps the chosen commits contiguous after the user checked or cleared one.
    /// </summary>
    /// <param name="item">The row the user changed.</param>
    private void OnChecked(UnsentCommitItemViewModel item)
    {
        var index = Items.IndexOf(item);
        if (index >= 0)
        {
            Choose(index, item.IsChecked);
        }

        Changed();
    }

    /// <summary>
    /// Checks a row and every older one, or clears a row and every newer one.
    /// </summary>
    /// <param name="index">The row, counted from the newest.</param>
    /// <param name="value">Whether the row is chosen.</param>
    private void Choose(int index, bool value)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (value && i >= index)
            {
                Items[i].SetByList(true);
            }
            else if (!value && i <= index)
            {
                Items[i].SetByList(false);
            }
        }
    }

    /// <summary>
    /// Tells the bindings that what depends on the list or on the choice changed.
    /// </summary>
    private void Changed()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedTip));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanRollBack));
        OnPropertyChanged(nameof(CountText));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raised when the list or the choice changed, so the commands that depend on it are asked again.
    /// </summary>
    public event EventHandler? SelectionChanged;
}
