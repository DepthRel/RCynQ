using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Formatting;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The synchronization tab: pending files, sending, patch history, diff viewing and conflict resolution.
/// </summary>
public sealed partial class SyncViewModel : PageViewModelBase, IChangedFolderOwner
{
    /// <summary>The number of patches the history loads at a time.</summary>
    public const int HISTORY_PAGE_SIZE = 20;

    /// <summary>The number of patches the collapsed history shows.</summary>
    public const int COLLAPSED_HISTORY_ROWS = 3;

    /// <summary>The name of the interface setting that keeps how the changed files are laid out.</summary>
    public const string FILES_VIEW_SETTING = "syncFilesView";

    /// <summary>The value of <see cref="FILES_VIEW_SETTING"/> for the tree of folders.</summary>
    private const string TREE_VIEW_VALUE = "tree";

    /// <summary>The value of <see cref="FILES_VIEW_SETTING"/> for the flat list of paths.</summary>
    private const string FLAT_VIEW_VALUE = "flat";

    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>The store of the interface settings, or <see langword="null"/> when the choices are not kept.</summary>
    private readonly IUiSettingsStore? _uiSettings;

    /// <summary>The folders the user closed in the tree, by path.</summary>
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.Ordinal);

    /// <summary>
    /// The folders the user closed while a filter is typed; forgotten when the filter changes, so every folder with a
    /// match opens.
    /// </summary>
    private readonly HashSet<string> _collapsedWhileFiltering = new(StringComparer.Ordinal);

    /// <summary>The role badge text.</summary>
    [ObservableProperty]
    private string _roleText = "—";

    /// <summary>The last synchronization time, or <see langword="null"/> when there was none.</summary>
    private DateTimeOffset? _lastSync;

    /// <summary>The sync base of the node; <see langword="null"/> until the first refresh.</summary>
    private SyncBaseStatus? _syncBase;

    /// <summary>Whether many files are checked at once, so what depends on the check marks is updated once at the end.</summary>
    private bool _isBatchSelecting;

    /// <summary>Grows each time a refresh replaces the history, so a page loaded meanwhile is not added to the new list.</summary>
    private int _historyVersion;

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

    /// <summary>Whether the changed files are shown as a tree of folders; otherwise as a flat list of paths.</summary>
    [ObservableProperty]
    private bool _isTreeView;

    /// <summary>Whether the search line under the title of the changed files is shown.</summary>
    [ObservableProperty]
    private bool _isSearchOpen;

    /// <summary>The text the paths of the shown files contain.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltering))]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(NoMatchesText))]
    private string _filterText = string.Empty;

    /// <summary>Whether the whole loaded history is shown; otherwise only its newest rows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreHistory))]
    [NotifyPropertyChangedFor(nameof(HistoryToggleTip))]
    private bool _isHistoryExpanded;

    /// <summary>Whether the service has older patches than the loaded ones.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreHistory))]
    [NotifyPropertyChangedFor(nameof(CanToggleHistory))]
    private bool _hasMoreHistory;

    /// <summary>Whether older patches are being loaded.</summary>
    [ObservableProperty]
    private bool _isLoadingMoreHistory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    /// <param name="uiSettings">The store that keeps how the changed files are laid out, or <see langword="null"/>.</param>
    public SyncViewModel(ILocalizer localizer, IServiceApiClient api, IDialogService dialogs, IUiSettingsStore? uiSettings = null)
        : base(localizer)
    {
        _api = api;
        _dialogs = dialogs;
        _uiSettings = uiSettings;
        _isTreeView = string.Equals(uiSettings?.Get(FILES_VIEW_SETTING), TREE_VIEW_VALUE, StringComparison.OrdinalIgnoreCase);
        ChangedFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoChangedFiles));
            OnPropertyChanged(nameof(CountText));
        };
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoMatches));
        History.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoHistory));
            OnPropertyChanged(nameof(CanToggleHistory));
        };
    }

    /// <summary>Gets the files that differ from the Git commit: the ones waiting to be sent and the ones the neighbour already has.</summary>
    public ObservableCollection<ChangedFileItemViewModel> ChangedFiles { get; } = new();

    /// <summary>Gets the rows on screen: the files that pass the filter, as a flat list or as a tree with folder rows.</summary>
    public ObservableCollection<ChangedRowViewModel> Rows { get; } = new();

    /// <summary>Gets the loaded part of the patch history, newest first.</summary>
    public ObservableCollection<PatchHistoryItemViewModel> History { get; } = new();

    /// <summary>Gets the patches on screen: the newest ones while the history is collapsed, all the loaded ones otherwise.</summary>
    public ObservableCollection<PatchHistoryItemViewModel> ShownHistory { get; } = new();

    /// <summary>Gets the last synchronization time in the language of the user.</summary>
    public string LastSyncText => DisplayText.Time(Localizer, _lastSync);

    /// <summary>Gets a value indicating whether there are no local changes.</summary>
    public bool HasNoChangedFiles => ChangedFiles.Count == 0;

    /// <summary>Gets a value indicating whether the list is narrowed by a filter.</summary>
    public bool IsFiltering => ChangedFileRows.Normalize(FilterText).Length > 0;

    /// <summary>Gets a value indicating whether there are changed files, but none of them passes the filter.</summary>
    public bool HasNoMatches => ChangedFiles.Count > 0 && Rows.Count == 0;

    /// <summary>Gets the text shown when no file passes the filter.</summary>
    public string NoMatchesText => Localizer.Format("Sync.NoMatches", ChangedFileRows.Normalize(FilterText));

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

            if (IsSelectionMode)
            {
                return $"({Localizer.Format("Sync.SelectedOf", SelectedCount, ChangedFiles.Count)})";
            }

            return IsFiltering
                ? $"({Localizer.Format("Sync.FoundOf", VisibleFiles().Count(), ChangedFiles.Count)})"
                : $"({DisplayText.Count(Localizer, "Sync.FileCount", ChangedFiles.Count)})";
        }
    }

    /// <summary>Gets a value indicating whether the history is empty.</summary>
    public bool HasNoHistory => History.Count == 0;

    /// <summary>Gets a value indicating whether the history has more rows than the collapsed one shows.</summary>
    public bool CanToggleHistory => History.Count > COLLAPSED_HISTORY_ROWS || HasMoreHistory;

    /// <summary>Gets a value indicating whether older patches can be loaded below the shown ones.</summary>
    public bool CanLoadMoreHistory => IsHistoryExpanded && HasMoreHistory;

    /// <summary>Gets the tool tip of the button that expands or collapses the history.</summary>
    public string HistoryToggleTip => Localizer.Get(IsHistoryExpanded ? "Sync.HistoryCollapse" : "Sync.HistoryExpand");

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

    /// <summary>Gets the name of the rebuild action, the title of the tool tip of its button.</summary>
    public string RebuildButtonText => Localizer.Get(_syncBase is { Mode: SyncBaseMode.Git } ? "Sync.RebuildBase" : "Sync.RecreateBaseline");

    /// <summary>Gets where the sync base comes from, for the header.</summary>
    public string SyncBaseText => _syncBase switch
    {
        { Mode: SyncBaseMode.Git } git => Localizer.Format("Sync.BaseGit", git.Branch ?? "HEAD", ShortCommit(git.Commit)),
        { Mode: SyncBaseMode.Manual } => Localizer.Get("Sync.BaseManual"),
        { } => Localizer.Get("Sync.BaseNone"),
        null => "—"
    };

    /// <inheritdoc />
    protected override Task<bool> LoadAsync() => RunQuietAsync(async () =>
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

        RebuildRows();
        OnSelectionChanged();

        // As many patches as are on screen are asked for again, so a refresh keeps the pages the user loaded; one more
        // than that tells whether older ones exist.
        var shown = Math.Clamp(History.Count, HISTORY_PAGE_SIZE, ApiLimits.MAX_PATCHES_PAGE - 1);

        // Fetched first, then replaced in one go. Two refreshes often overlap (a patch event arrives while the page is
        // refreshed after a send); clearing before the request would let both clear and then both add, doubling the list.
        var patches = await _api.GetPatchesAsync(0, shown + 1);
        _historyVersion++;
        History.Clear();
        foreach (var patch in patches.Take(shown))
        {
            History.Add(new PatchHistoryItemViewModel(Localizer, patch));
        }

        HasMoreHistory = patches.Count > shown;
        UpdateShownHistory();
    });

    /// <inheritdoc />
    public override void RefreshTexts()
    {
        base.RefreshTexts();
        OnPropertyChanged(nameof(SyncBaseText));
        OnPropertyChanged(nameof(RebuildButtonText));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(NoMatchesText));
        OnPropertyChanged(nameof(HistoryToggleTip));
        foreach (var item in History)
        {
            item.RefreshTexts();
        }
    }

    /// <inheritdoc />
    public bool IsFolderExpanded(string path) => !CollapsedFolders().Contains(path);

    /// <inheritdoc />
    public void ToggleFolder(ChangedFolderItemViewModel folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var collapsed = CollapsedFolders();
        if (!collapsed.Remove(folder.Path))
        {
            collapsed.Add(folder.Path);
        }

        RebuildRows();
    }

    /// <inheritdoc />
    public void SetSelected(IReadOnlyList<ChangedFileItemViewModel> files, bool selected)
    {
        ArgumentNullException.ThrowIfNull(files);

        _isBatchSelecting = true;
        try
        {
            foreach (var file in files)
            {
                file.IsSelected = selected;
            }
        }
        finally
        {
            _isBatchSelecting = false;
        }

        OnSelectionChanged();
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
            SetSelected(ChangedFiles, false);
            NotifyDataChanged();
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
            SetSelected(ChangedFiles, false);
        }
    }

    /// <summary>
    /// Checks every file that passes the filter.
    /// </summary>
    [RelayCommand]
    private void SelectAll() => SetSelected(VisibleFiles().ToArray(), true);

    /// <summary>
    /// Unchecks every file that passes the filter.
    /// </summary>
    [RelayCommand]
    private void ClearSelection() => SetSelected(VisibleFiles().ToArray(), false);

    /// <summary>
    /// Shows the changed files as a flat list of paths.
    /// </summary>
    [RelayCommand]
    private void ShowFlat() => IsTreeView = false;

    /// <summary>
    /// Shows the changed files as a tree of folders.
    /// </summary>
    [RelayCommand]
    private void ShowTree() => IsTreeView = true;

    /// <summary>
    /// Shows the search line, or hides it and drops the filter.
    /// </summary>
    [RelayCommand]
    private void ToggleSearch() => IsSearchOpen = !IsSearchOpen;

    /// <summary>
    /// Hides the search line and drops the filter.
    /// </summary>
    [RelayCommand]
    private void CloseSearch() => IsSearchOpen = false;

    /// <summary>
    /// Drops the filter, keeping the search line open.
    /// </summary>
    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    /// <summary>
    /// Expands the history to every loaded patch, or collapses it to the newest ones.
    /// </summary>
    [RelayCommand]
    private void ToggleHistory() => IsHistoryExpanded = !IsHistoryExpanded;

    /// <summary>
    /// Loads the next page of older patches below the shown ones.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task LoadMoreHistoryAsync()
    {
        if (!CanLoadMoreHistory || IsLoadingMoreHistory)
        {
            return;
        }

        IsLoadingMoreHistory = true;
        try
        {
            var version = _historyVersion;
            await RunQuietAsync(async () =>
            {
                var patches = await _api.GetPatchesAsync(History.Count, HISTORY_PAGE_SIZE + 1);

                // A refresh replaced the list meanwhile: the page no longer follows it.
                if (version != _historyVersion)
                {
                    return;
                }

                // A patch that arrived meanwhile moves the older ones down, so a row of the page may already be shown.
                var known = History.Select(item => item.Id).ToHashSet();
                foreach (var patch in patches.Take(HISTORY_PAGE_SIZE).Where(patch => !known.Contains(patch.Id)))
                {
                    History.Add(new PatchHistoryItemViewModel(Localizer, patch));
                }

                HasMoreHistory = patches.Count > HISTORY_PAGE_SIZE;
                UpdateShownHistory();
            });
        }
        finally
        {
            IsLoadingMoreHistory = false;
        }
    }

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

        foreach (var folder in Rows.OfType<ChangedFolderItemViewModel>())
        {
            folder.IsSelectable = value;
        }
    }

    /// <summary>
    /// Lays the rows out again in the chosen way and keeps the choice for the next start.
    /// </summary>
    /// <param name="value">Whether the files are shown as a tree.</param>
    partial void OnIsTreeViewChanged(bool value)
    {
        RebuildRows();
        try
        {
            _uiSettings?.Set(FILES_VIEW_SETTING, value ? TREE_VIEW_VALUE : FLAT_VIEW_VALUE);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the choice for the next start is lost; the list is already laid out.
        }
    }

    /// <summary>
    /// Drops the filter when the search line is hidden, so no file stays hidden without a visible reason.
    /// </summary>
    /// <param name="value">Whether the search line is shown.</param>
    partial void OnIsSearchOpenChanged(bool value)
    {
        if (!value)
        {
            FilterText = string.Empty;
        }
    }

    /// <summary>
    /// Shows the files that pass the new filter, with every folder that holds one of them open.
    /// </summary>
    /// <param name="value">The text of the filter.</param>
    partial void OnFilterTextChanged(string value)
    {
        _collapsedWhileFiltering.Clear();
        RebuildRows();
    }

    /// <summary>
    /// Shows the newest patches or all the loaded ones.
    /// </summary>
    /// <param name="value">Whether the history is expanded.</param>
    partial void OnIsHistoryExpandedChanged(bool value) => UpdateShownHistory();

    /// <summary>
    /// Lists the files that pass the filter, in the order of the service.
    /// </summary>
    /// <returns>The files.</returns>
    private IEnumerable<ChangedFileItemViewModel> VisibleFiles()
    {
        var filter = FilterText;
        return ChangedFiles.Where(file => ChangedFileRows.Matches(file.Path, filter));
    }

    /// <summary>
    /// Gets the set of closed folders that applies now: the one of the filter while a filter is typed.
    /// </summary>
    /// <returns>The set of paths.</returns>
    private HashSet<string> CollapsedFolders() => IsFiltering ? _collapsedWhileFiltering : _collapsedFolders;

    /// <summary>
    /// Lays the rows out again from the changed files, the filter, the chosen layout and the closed folders.
    /// </summary>
    private void RebuildRows()
    {
        var rows = IsTreeView ? ChangedFileRows.Tree(VisibleFiles(), this) : ChangedFileRows.Flat(VisibleFiles());

        Rows.Clear();
        foreach (var row in rows)
        {
            row.IsSelectable = IsSelectionMode;
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>
    /// Fills the shown history from the loaded one.
    /// </summary>
    private void UpdateShownHistory()
    {
        var shown = IsHistoryExpanded ? History.ToArray() : History.Take(COLLAPSED_HISTORY_ROWS).ToArray();

        ShownHistory.Clear();
        foreach (var item in shown)
        {
            ShownHistory.Add(item);
        }
    }

    /// <summary>
    /// Updates what depends on the check marks.
    /// </summary>
    private void OnSelectionChanged()
    {
        if (_isBatchSelecting)
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CountText));
        SendCommand.NotifyCanExecuteChanged();
        foreach (var folder in Rows.OfType<ChangedFolderItemViewModel>())
        {
            folder.RefreshSelection();
        }
    }

    /// <summary>
    /// Forces a fresh check of what is pending, without waiting for the next tab switch or server event.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task ScanAsync() => ReloadAsync();

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
        var confirmText = Localizer.Get(isGit ? "Sync.RebuildConfirm" : wasInitialized ? "Sync.BaselineRecreateConfirm" : "Sync.BaselineConfirm");
        if (!await _dialogs.ConfirmAsync(Localizer.Get(isGit ? "Sync.RebuildTitle" : "Sync.BaselineTitle"), confirmText))
        {
            return;
        }

        if (await RunSafeAsync(async () => await _api.CreateBaselineAsync()))
        {
            InfoMessage = Localizer.Get(isGit ? "Sync.Rebuilt" : wasInitialized ? "Sync.BaselineRecreated" : "Sync.BaselineCreated");
            NotifyDataChanged();
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

        var dialog = new ConflictDialogViewModel(Localizer, item.Id, item.Patch.SourceNode.Value, item.Patch.Conflicts);
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
                // A merge that left conflict markers is done, but the files it names still need an edit.
                PatchStatus.ConflictResolved when result.Conflicts.Count > 0 => Localizer.Format(
                    "Sync.ResolvedWithMarkers", result.Conflicts.Count, string.Join(", ", result.Conflicts.Select(conflict => conflict.Path))),
                PatchStatus.ConflictResolved => Localizer.Get("Sync.Resolved"),
                PatchStatus.Rejected => Localizer.Get("Sync.PatchRejected"),
                PatchStatus.ConflictPending => Localizer.Format("Sync.ConflictsLeft", result.Conflicts.Count),
                _ => LocalizedText.Render(result.Error, Localizer) ?? result.Status.ToString()
            };
        }))
        {
            NotifyDataChanged();
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
            await _dialogs.ShowTextAsync(Localizer.Format("Sync.DiffTitle", item.Id), string.IsNullOrEmpty(diff) ? Localizer.Get("Sync.EmptyDiff") : diff!);
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
