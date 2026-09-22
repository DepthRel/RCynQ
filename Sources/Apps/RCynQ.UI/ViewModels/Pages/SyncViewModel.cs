using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Enums;
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

    /// <summary>Whether the baseline exists.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsBaseline))]
    private bool _baselineInitialized = true;

    /// <summary>The number of conflicting incoming patches.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    private int _conflictCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncViewModel"/> class.
    /// </summary>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    public SyncViewModel(IServiceApiClient api, IDialogService dialogs)
    {
        _api = api;
        _dialogs = dialogs;
        PendingFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoPendingFiles));
        History.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoHistory));
    }

    /// <summary>Gets the files waiting to be sent.</summary>
    public ObservableCollection<string> PendingFiles { get; } = new();

    /// <summary>Gets the patch history.</summary>
    public ObservableCollection<PatchHistoryItemViewModel> History { get; } = new();

    /// <summary>Gets the last synchronization time in the language of the user.</summary>
    public string LastSyncText => DisplayText.Time(_lastSync);

    /// <summary>Gets a value indicating whether there are no local changes.</summary>
    public bool HasNoPendingFiles => PendingFiles.Count == 0;

    /// <summary>Gets a value indicating whether the history is empty.</summary>
    public bool HasNoHistory => History.Count == 0;

    /// <summary>Gets a value indicating whether conflicting patches wait for a decision.</summary>
    public bool HasConflicts => ConflictCount > 0;

    /// <summary>Gets a value indicating whether the baseline must be created first.</summary>
    public bool NeedsBaseline => !BaselineInitialized;

    /// <inheritdoc />
    public override Task RefreshAsync() => RunSafeAsync(async () =>
    {
        var status = await _api.GetStateAsync();
        RoleText = status.Role.ToString();
        _lastSync = new[] { status.State.LastSentAtUtc, status.State.LastReceivedAtUtc }.Max();
        OnPropertyChanged(nameof(LastSyncText));
        BaselineInitialized = status.BaselineInitialized;
        ConflictCount = status.ConflictPendingCount;

        PendingFiles.Clear();
        foreach (var change in status.PendingChanges)
        {
            PendingFiles.Add($"{DisplayText.Change(change.Kind)}  {change.Path}");
        }

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
        foreach (var item in History)
        {
            item.RefreshTexts();
        }
    }

    /// <summary>
    /// Sends the pending changes.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task SendAsync()
    {
        if (await RunSafeAsync(async () => InfoMessage = (await _api.SendPatchAsync()).Message))
        {
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Forces a fresh check of what is pending, without waiting for the next tab switch or server event.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task ScanAsync() => await RefreshAsync();

    /// <summary>
    /// Creates or replaces the baseline after confirmation.
    /// </summary>
    /// <remarks>
    /// The confirmation text and the result message differ depending on whether a baseline already exists: replacing one is a
    /// materially different action (it can quietly hide already-differing files from future sends) and gets a stronger warning.
    /// </remarks>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task CreateBaselineAsync()
    {
        var wasInitialized = BaselineInitialized;
        var confirmText = wasInitialized ? Texts.Get("Sync.BaselineRecreateConfirm") : Texts.Get("Sync.BaselineConfirm");
        if (!await _dialogs.ConfirmAsync(Texts.Get("Sync.BaselineTitle"), confirmText))
        {
            return;
        }

        if (await RunSafeAsync(async () => await _api.CreateBaselineAsync()))
        {
            InfoMessage = Texts.Get(wasInitialized ? "Sync.BaselineRecreated" : "Sync.BaselineCreated");
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
}
