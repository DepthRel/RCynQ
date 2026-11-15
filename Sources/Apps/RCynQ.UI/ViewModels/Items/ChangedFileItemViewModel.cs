using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A file row of the list of changed files on the "Синхронизация" tab.
/// </summary>
public sealed partial class ChangedFileItemViewModel : ChangedRowViewModel
{
    /// <summary>The action called when the row is checked or unchecked.</summary>
    private readonly Action? _selectionChanged;

    /// <summary>What can be done with the file, or <see langword="null"/> when the list only shows files.</summary>
    private readonly IChangedFileActions? _actions;

    /// <summary>Whether the file is chosen to be sent.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether something is being done with the file now, so the actions that change it wait.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseAllChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertChangesCommand))]
    private bool _isWorking;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangedFileItemViewModel"/> class.
    /// </summary>
    /// <param name="file">The changed file.</param>
    /// <param name="selectionChanged">The action called when the row is checked or unchecked, or <see langword="null"/>.</param>
    /// <param name="actions">What can be done with the file, or <see langword="null"/> when the list only shows files.</param>
    public ChangedFileItemViewModel(ChangedFile file, Action? selectionChanged = null, IChangedFileActions? actions = null)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
        _selectionChanged = selectionChanged;
        _actions = actions;
        Label = file.Path;
    }

    /// <summary>Gets the changed file.</summary>
    public ChangedFile File { get; }

    /// <summary>Gets the relative path of the file.</summary>
    public string Path => File.Path;

    /// <summary>Gets the one-letter marker of the change.</summary>
    public string KindText => DisplayText.Change(File.Kind);

    /// <summary>Gets a value indicating whether the other node already has this version of the file.</summary>
    public bool AlreadySent => File.AlreadySent;

    /// <summary>Gets a value indicating whether only a part of the changes of the file goes.</summary>
    public bool IsPartial => File.Partial != PartialChangeState.None;

    /// <summary>Gets a value indicating whether only a part goes and the file was not edited since the choice.</summary>
    public bool IsPartialCurrent => File.Partial == PartialChangeState.Partial;

    /// <summary>Gets a value indicating whether only a part goes and the file was edited after the choice.</summary>
    public bool IsPartialOutdated => File.Partial == PartialChangeState.Outdated;

    /// <summary>Gets a value indicating whether the diff of the file can be shown.</summary>
    public bool HasActions => _actions is not null;

    /// <summary>Gets a value indicating whether a part of the changes of the file can be chosen.</summary>
    public bool CanChooseChanges => _actions is not null && !File.AlreadySent && File.Kind != ChangeKind.Deleted;

    /// <summary>Gets a value indicating whether the changes of the file can be taken back here.</summary>
    public bool CanRevertChanges => _actions is { CanRevertChanges: true };

    /// <summary>Gets a value indicating whether a choice of the changes can start now.</summary>
    private bool CanStartChoice => CanChooseChanges && !IsWorking;

    /// <summary>Gets a value indicating whether every change can be chosen again now.</summary>
    private bool CanChooseAllNow => IsPartial && !IsWorking;

    /// <summary>Gets a value indicating whether the changes can be taken back now.</summary>
    private bool CanRevertNow => CanRevertChanges && !IsWorking;

    /// <summary>
    /// Shows the diff of the file.
    /// </summary>
    /// <returns>A task that completes when the diff is shown.</returns>
    [RelayCommand(CanExecute = nameof(HasActions))]
    private Task ShowDiffAsync() => _actions?.ShowDiffAsync(this) ?? Task.CompletedTask;

    /// <summary>
    /// Lets the user choose the changes of the file that go.
    /// </summary>
    /// <returns>A task that completes when the choice is made or given up.</returns>
    [RelayCommand(CanExecute = nameof(CanStartChoice))]
    private Task ChooseChangesAsync() => _actions?.ChooseChangesAsync(this) ?? Task.CompletedTask;

    /// <summary>
    /// Chooses every change of the file again, so the whole file goes.
    /// </summary>
    /// <returns>A task that completes when the choice is gone.</returns>
    [RelayCommand(CanExecute = nameof(CanChooseAllNow))]
    private Task ChooseAllChangesAsync() => _actions?.ChooseAllChangesAsync(this) ?? Task.CompletedTask;

    /// <summary>
    /// Takes back the changes of the file that are not staged, once the user confirms it.
    /// </summary>
    /// <returns>A task that completes when the changes are taken back or the user gave it up.</returns>
    [RelayCommand(CanExecute = nameof(CanRevertNow))]
    private Task RevertChangesAsync() => _actions?.RevertChangesAsync(this) ?? Task.CompletedTask;

    /// <summary>
    /// Tells the owner that the selection changed.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnIsSelectedChanged(bool value) => _selectionChanged?.Invoke();
}
