using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Formatting;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The synchronization tab: pending files, sending, patch history, diff viewing and conflict resolution.
/// </summary>
public sealed partial class SyncViewModel : PageViewModelBase
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>The role badge text.</summary>
    [ObservableProperty]
    private string _roleText = "—";

    /// <summary>The last synchronization time, or <see langword="null"/> when there was none.</summary>
    private DateTimeOffset? _lastSync;

    /// <summary>The sync base of the node; <see langword="null"/> until the first refresh.</summary>
    private SyncBaseStatus? _syncBase;

    /// <summary>The number of conflicting incoming patches.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    private int _conflictCount;

    /// <summary>Whether the user picks the files to send by check marks; otherwise every changed file is sent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isSelectionMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncViewModel"/> class.
    /// </summary>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    public SyncViewModel(IServiceApiClient api, IDialogService dialogs)
    {
        _api = api;
        _dialogs = dialogs;
        ChangedFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoChangedFiles));
            OnPropertyChanged(nameof(CountText));
        };
        History.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoHistory));
    }

    /// <summary>Gets the files that differ from the Git commit: the ones waiting to be sent and the ones the neighbour already has.</summary>
    public ObservableCollection<ChangedFileItemViewModel> ChangedFiles { get; } = new();

    /// <summary>Gets the patch history.</summary>
    public ObservableCollection<PatchHistoryItemViewModel> History { get; } = new();

    /// <summary>Gets the last synchronization time in the language of the user.</summary>
    public string LastSyncText => DisplayText.Time(_lastSync);

    /// <summary>Gets a value indicating whether there are no local changes.</summary>
    public bool HasNoChangedFiles => ChangedFiles.Count == 0;

    /// <summary>Gets the number of files checked to be sent.</summary>
    public int SelectedCount => ChangedFiles.Count(file => file.IsSelected);

    /// <summary>Gets a value indicating whether the send button can be pressed: with check marks at least one file has to be checked.</summary>
    public bool CanSend => !IsSelectionMode || SelectedCount > 0;

    /// <summary>Gets the number of files next to the title of the list, or an empty text when there are none.</summary>
    public string CountText
    {
        get
        {
            if (ChangedFiles.Count == 0)
            {
                return string.Empty;
            }

            return IsSelectionMode
                ? $"({Texts.Format("Sync.SelectedOf", SelectedCount, ChangedFiles.Count)})"
                : $"({DisplayText.Count("Sync.FileCount", ChangedFiles.Count)})";
        }
    }

    /// <summary>Gets a value indicating whether the history is empty.</summary>
    public bool HasNoHistory => History.Count == 0;

    /// <summary>Gets a value indicating whether conflicting patches wait for a decision.</summary>
    public bool HasConflicts => ConflictCount > 0;

    /// <summary>Gets a value indicating whether patches cannot be exchanged yet because there is no sync base.</summary>
    public bool NeedsSyncBase => _syncBase is { IsReady: false };

    /// <summary>Gets why there is no sync base, in the language of the service.</summary>
    public string SyncBaseProblem => _syncBase?.Problem ?? string.Empty;

    /// <summary>Gets a value indicating whether a manual snapshot may be taken, because the working tree has no Git.</summary>
    public bool CanCreateManualBaseline => _syncBase is { CanCreateManual: true };

    /// <summary>Gets a value indicating whether the sync base may be rebuilt: from the Git commit, or as a new manual snapshot.</summary>
    public bool CanRebuildBaseline => _syncBase is { IsReady: true };

    /// <summary>Gets the text of the rebuild button.</summary>
    public string RebuildButtonText => Texts.Get(_syncBase is { Mode: SyncBaseMode.Git } ? "Sync.RebuildBase" : "Sync.RecreateBaseline");

    /// <summary>Gets where the sync base comes from, for the header.</summary>
    public string SyncBaseText => _syncBase switch
    {
        { Mode: SyncBaseMode.Git } git => Texts.Format("Sync.BaseGit", git.Branch ?? "HEAD", ShortCommit(git.Commit)),
        { Mode: SyncBaseMode.Manual } => Texts.Get("Sync.BaseManual"),
        { } => Texts.Get("Sync.BaseNone"),
        null => "—"
    };

    /// <inheritdoc />
    public override Task RefreshAsync() => RunSafeAsync(async () =>
    {
        var status = await _api.GetStateAsync();
        RoleText = status.Role.ToString();
        _lastSync = new[] { status.State.LastSentAtUtc, status.State.LastReceivedAtUtc }.Max();
        OnPropertyChanged(nameof(LastSyncText));
        ApplySyncBase(status.SyncBase);
        ConflictCount = status.ConflictPendingCount;

        // A service of an older version reports only what is pending.
        var files = status.ChangedFiles ?? status.PendingChanges.Select(change => new ChangedFile(change.Kind, change.Path, false)).ToArray();
        var selected = ChangedFiles.Where(file => file.IsSelected).Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        ChangedFiles.Clear();
        foreach (var file in files)
        {
            ChangedFiles.Add(new ChangedFileItemViewModel(file, OnSelectionChanged) { IsSelectable = IsSelectionMode, IsSelected = selected.Contains(file.Path) });
        }

        OnSelectionChanged();

        History.Clear();
        foreach (var patch in await _api.GetPatchesAsync(100))
        {
            History.Add(new PatchHistoryItemViewModel(patch));
        }
    });

    /// <inheritdoc />
    public override void RefreshTexts()
    {
        base.RefreshTexts();
        OnPropertyChanged(nameof(SyncBaseText));
        OnPropertyChanged(nameof(RebuildButtonText));
        OnPropertyChanged(nameof(CountText));
        foreach (var item in History)
        {
            item.RefreshTexts();
        }
    }

    /// <summary>
    /// Sends the changed files: the checked ones in selection mode, all of them otherwise.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var paths = IsSelectionMode ? ChangedFiles.Where(file => file.IsSelected).Select(file => file.Path).ToArray() : null;
        if (await RunSafeAsync(async () => InfoMessage = (await _api.SendPatchAsync(paths)).Message))
        {
            SetAllSelected(false);
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Switches the check marks in front of the files on or off; switching them off forgets the choice, so everything is sent again.
    /// </summary>
    [RelayCommand]
    private void ToggleSelectionMode()
    {
        IsSelectionMode = !IsSelectionMode;
        if (!IsSelectionMode)
        {
            SetAllSelected(false);
        }
    }

    /// <summary>
    /// Checks every file.
    /// </summary>
    [RelayCommand]
    private void SelectAll() => SetAllSelected(true);

    /// <summary>
    /// Unchecks every file.
    /// </summary>
    [RelayCommand]
    private void ClearSelection() => SetAllSelected(false);

    /// <summary>
    /// Shows or hides the check marks of the rows.
    /// </summary>
    /// <param name="value">Whether the user picks the files to send.</param>
    partial void OnIsSelectionModeChanged(bool value)
    {
        foreach (var file in ChangedFiles)
        {
            file.IsSelectable = value;
        }
    }

    /// <summary>
    /// Checks or unchecks every file.
    /// </summary>
    /// <param name="selected">Whether the files are checked.</param>
    private void SetAllSelected(bool selected)
    {
        foreach (var file in ChangedFiles)
        {
            file.IsSelected = selected;
        }
    }

    /// <summary>
    /// Updates what depends on the check marks.
    /// </summary>
    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CountText));
        SendCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Forces a fresh check of what is pending, without waiting for the next tab switch or server event.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task ScanAsync() => await RefreshAsync();

    /// <summary>
    /// Rebuilds the sync base after confirmation: from the Git commit, or as a manual snapshot for a working tree without Git.
    /// </summary>
    /// <remarks>
    /// The confirmation text and the result message differ depending on whether a snapshot already exists: replacing one is a
    /// materially different action (it can quietly hide already-differing files from future sends) and gets a stronger warning.
    /// </remarks>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task CreateBaselineAsync()
    {
        var isGit = _syncBase is { Mode: SyncBaseMode.Git };
        var wasInitialized = _syncBase is { IsReady: true };
        var confirmText = Texts.Get(isGit ? "Sync.RebuildConfirm" : wasInitialized ? "Sync.BaselineRecreateConfirm" : "Sync.BaselineConfirm");
        if (!await _dialogs.ConfirmAsync(Texts.Get(isGit ? "Sync.RebuildTitle" : "Sync.BaselineTitle"), confirmText))
        {
            return;
        }

        if (await RunSafeAsync(async () => await _api.CreateBaselineAsync()))
        {
            InfoMessage = Texts.Get(isGit ? "Sync.Rebuilt" : wasInitialized ? "Sync.BaselineRecreated" : "Sync.BaselineCreated");
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Opens the conflict dialog and applies the chosen policy.
    /// </summary>
    /// <param name="item">The patch.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task ResolveAsync(PatchHistoryItemViewModel? item)
    {
        if (item is null || !item.CanResolve)
        {
            return;
        }

        var dialog = new ConflictDialogViewModel(item.Id, item.Patch.SourceNode.Value, item.Patch.Conflicts);
        var policy = await _dialogs.ShowConflictDialogAsync(dialog);
        if (policy is null)
        {
            return;
        }

        if (await RunSafeAsync(async () =>
        {
            var result = await _api.ResolvePatchAsync(item.Id, policy.Value);
            InfoMessage = result.Status switch
            {
                PatchStatus.ConflictResolved => Texts.Get("Sync.Resolved"),
                PatchStatus.Rejected => Texts.Get("Sync.PatchRejected"),
                PatchStatus.ConflictPending => Texts.Format("Sync.ConflictsLeft", result.Conflicts.Count),
                _ => LocalizedText.Render(result.Error) ?? result.Status.ToString()
            };
        }))
        {
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Shows the diff of a patch.
    /// </summary>
    /// <param name="item">The patch.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task ViewDiffAsync(PatchHistoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        string? diff = null;
        if (await RunSafeAsync(async () => diff = await _api.GetPatchDiffAsync(item.Id)))
        {
            await _dialogs.ShowTextAsync(Texts.Format("Sync.DiffTitle", item.Id), string.IsNullOrEmpty(diff) ? Texts.Get("Sync.EmptyDiff") : diff!);
        }
    }

    /// <summary>
    /// Shows the sync base reported by the service.
    /// </summary>
    /// <param name="syncBase">The sync base status.</param>
    public void ApplySyncBase(SyncBaseStatus syncBase)
    {
        ArgumentNullException.ThrowIfNull(syncBase);
        _syncBase = syncBase;
        OnPropertyChanged(nameof(NeedsSyncBase));
        OnPropertyChanged(nameof(SyncBaseProblem));
        OnPropertyChanged(nameof(CanCreateManualBaseline));
        OnPropertyChanged(nameof(CanRebuildBaseline));
        OnPropertyChanged(nameof(RebuildButtonText));
        OnPropertyChanged(nameof(SyncBaseText));
    }

    /// <summary>
    /// Shortens a commit name for display.
    /// </summary>
    /// <param name="commit">The full commit name, or <see langword="null"/>.</param>
    /// <returns>The first characters of the name, or a dash.</returns>
    private static string ShortCommit(string? commit) => string.IsNullOrEmpty(commit) ? "—" : commit[..Math.Min(8, commit.Length)];
}
