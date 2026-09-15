using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Formatting;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The "Синхронизация" tab: pending files, sending, patch history, diff viewing and conflict resolution.
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

    /// <summary>The last synchronization time.</summary>
    [ObservableProperty]
    private string _lastSyncText = "никогда";

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
        LastSyncText = DisplayText.Time(new[] { status.State.LastSentAtUtc, status.State.LastReceivedAtUtc }.Max());
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
    /// Creates the baseline after confirmation.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task CreateBaselineAsync()
    {
        if (!await _dialogs.ConfirmAsync("Базовая копия", "Создать базовую копию из текущей рабочей копии? Делайте это на Source и Dest, когда их рабочие копии совпадают."))
        {
            return;
        }

        if (await RunSafeAsync(async () => await _api.CreateBaselineAsync()))
        {
            InfoMessage = "Базовая копия создана.";
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
                PatchStatus.ConflictResolved => "Конфликт разрешён.",
                PatchStatus.Rejected => "Патч отклонён, локальное состояние сохранено.",
                PatchStatus.ConflictPending => $"Остались конфликты ({result.Conflicts.Count}) — разрешите их вручную средствами Git.",
                _ => result.Error ?? result.Status.ToString()
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
            await _dialogs.ShowTextAsync($"Диф патча {item.Id}", string.IsNullOrEmpty(diff) ? "(пустой диф)" : diff!);
        }
    }
}
