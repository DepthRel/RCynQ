using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The window of the diff of a changed file: every change with the lines around it, and for a file that can go in part
/// a check mark on every change.
/// </summary>
public sealed partial class FileDiffDialogViewModel : DialogViewModelBase
{
    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The changes as shown when the window opened, to tell whether the user changed the choice.</summary>
    private readonly HashSet<int> _initial;

    /// <summary>The role of the node the changes go to, named in the texts.</summary>
    private readonly NodeRole _peerRole;

    /// <summary>Whether the file is left out of the next patch, so that none of its changes goes now.</summary>
    private readonly bool _leftOut;

    /// <summary>The text of how many changes go.</summary>
    [ObservableProperty]
    private string _countText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileDiffDialogViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="diff">The diff.</param>
    /// <param name="peerRole">The role of the node the changes go to: the Origin of a Workstation, the Workstation of an Origin.</param>
    /// <param name="leftOut">
    /// Whether the check mark of the file is off: no change is checked at the start, whatever was chosen before, and
    /// checking one is what lets the file go.
    /// </param>
    public FileDiffDialogViewModel(ILocalizer localizer, FileDiffResponse diff, NodeRole peerRole, bool leftOut = false)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        Diff = diff ?? throw new ArgumentNullException(nameof(diff));
        _peerRole = peerRole;
        _leftOut = leftOut && diff.CanChoose;
        Title = localizer.Format("Diff.Title", diff.Path);
        CanChoose = diff.CanChoose;
        NoticeText = Notice(diff);

        foreach (var block in diff.Blocks)
        {
            var line = block.WorkCount > 0 ? block.WorkStart + 1 : block.BaseStart + 1;
            var head = new DiffBlockRowViewModel(block.Index, localizer.Format("Diff.Change", block.Index + 1, diff.Blocks.Count, line), CanChoose, block.Selected && !_leftOut, OnSelectionChanged);
            Blocks.Add(head);
            Rows.Add(head);
            foreach (var diffLine in block.Lines)
            {
                Rows.Add(new DiffLineRowViewModel(diffLine));
            }
        }

        _initial = Blocks.Where(block => block.IsSelected).Select(block => block.Index).ToHashSet();
        UpdateCount();
    }

    /// <summary>Gets the diff.</summary>
    public FileDiffResponse Diff { get; }

    /// <summary>Gets the title of the window.</summary>
    public string Title { get; }

    /// <summary>Gets a value indicating whether a part of the changes can be chosen.</summary>
    public bool CanChoose { get; }

    /// <summary>Gets what the user should know about the diff, or an empty text.</summary>
    public string NoticeText { get; }

    /// <summary>Gets a value indicating whether there is something the user should know.</summary>
    public bool HasNotice => NoticeText.Length > 0;

    /// <summary>Gets the heads of the changes.</summary>
    public ObservableCollection<DiffBlockRowViewModel> Blocks { get; } = new();

    /// <summary>Gets the rows of the diff, top to bottom.</summary>
    public ObservableCollection<DiffRowViewModel> Rows { get; } = new();

    /// <summary>Gets the changes the user chose.</summary>
    public IReadOnlyList<int> ChosenBlocks => Blocks.Where(block => block.IsSelected).Select(block => block.Index).ToArray();

    /// <summary>
    /// Gets a value indicating whether applying changes anything: the choice differs from the one shown at the start, or
    /// the stored choice does not fit the file as it is. A file left out changes only when something is checked: with
    /// nothing checked it stays out and keeps its earlier choice.
    /// </summary>
    public bool HasNewChoice => _leftOut ? ChosenBlocks.Count > 0 : Diff.CustomChoice || Diff.Partial == PartialChangeState.Outdated || !_initial.SetEquals(ChosenBlocks);

    /// <summary>
    /// Checks every change.
    /// </summary>
    [RelayCommand]
    private void SelectAll() => SetAll(true);

    /// <summary>
    /// Unchecks every change.
    /// </summary>
    [RelayCommand]
    private void SelectNone() => SetAll(false);

    /// <summary>
    /// Closes the window and keeps the choice.
    /// </summary>
    [RelayCommand]
    private void Apply() => Close(true);

    /// <summary>
    /// Closes the window without changing the choice.
    /// </summary>
    [RelayCommand]
    private void Cancel() => Close(false);

    /// <summary>
    /// Checks or unchecks every change.
    /// </summary>
    /// <param name="selected">Whether every change goes.</param>
    private void SetAll(bool selected)
    {
        foreach (var block in Blocks)
        {
            block.IsSelected = selected;
        }
    }

    /// <summary>
    /// Updates what depends on the check marks.
    /// </summary>
    private void OnSelectionChanged() => UpdateCount();

    /// <summary>
    /// Updates the text of how many changes go.
    /// </summary>
    private void UpdateCount() => CountText = _localizer.Format("Diff.Count", Blocks.Count(block => block.IsSelected), Blocks.Count, _peerRole);

    /// <summary>
    /// Finds what the user should know about a diff.
    /// </summary>
    /// <param name="diff">The diff.</param>
    /// <returns>The text, or an empty text.</returns>
    private string Notice(FileDiffResponse diff)
    {
        var key = diff.Availability switch
        {
            DiffAvailability.Binary => "Diff.Binary",
            DiffAvailability.TooLarge => "Diff.TooLarge",
            DiffAvailability.Link => "Diff.Link",
            DiffAvailability.Same => "Diff.Same",
            _ when diff.Blocks.Count == 0 => "Diff.OnlyLineEndings",
            _ when !diff.CanChoose => "Diff.ViewOnly",
            _ when _leftOut => null,
            _ when diff.CustomChoice => "Diff.CustomChoice",
            _ when diff.Partial == PartialChangeState.Outdated => "Diff.Outdated",
            _ => null
        };
        // Only the note that the other node already has the file names it; the other notes ignore the role.
        return key is null ? string.Empty : _localizer.Format(key, _peerRole);
    }
}
