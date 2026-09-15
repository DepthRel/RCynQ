using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The "Логи" tab: service log entries filtered by level, with export.
/// </summary>
public sealed partial class LogsViewModel : PageViewModelBase
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>The selected minimum level.</summary>
    [ObservableProperty]
    private string _selectedLevel = "Information";

    /// <summary>
    /// Initializes a new instance of the <see cref="LogsViewModel"/> class.
    /// </summary>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    public LogsViewModel(IServiceApiClient api, IDialogService dialogs)
    {
        _api = api;
        _dialogs = dialogs;
        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoEntries));
    }

    /// <summary>Gets the selectable levels.</summary>
    public IReadOnlyList<string> Levels { get; } = new[] { "Information", "Warning", "Error" };

    /// <summary>Gets the entries, newest first.</summary>
    public ObservableCollection<LogItemViewModel> Entries { get; } = new();

    /// <summary>Gets a value indicating whether no entries are shown.</summary>
    public bool HasNoEntries => Entries.Count == 0;

    /// <inheritdoc />
    public override Task RefreshAsync() => RunSafeAsync(async () =>
    {
        var entries = await _api.GetLogsAsync(SelectedLevel, 500);
        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(new LogItemViewModel(entry));
        }
    });

    /// <summary>
    /// Reloads the entries on user request.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task ReloadAsync() => RefreshAsync();

    /// <summary>
    /// Reloads the entries when the level changes.
    /// </summary>
    /// <param name="value">The new level.</param>
    partial void OnSelectedLevelChanged(string value) => _ = RefreshAsync();

    /// <summary>
    /// Exports the shown entries to a text file.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task ExportAsync()
    {
        var file = await _dialogs.PickSaveFileAsync("Экспорт логов", $"rcynq-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        if (file is null)
        {
            return;
        }

        await RunSafeAsync(async () =>
        {
            await File.WriteAllLinesAsync(file, Entries.Reverse().Select(entry => entry.ToExportLine()));
            InfoMessage = $"Логи сохранены в {file}.";
        });
    }
}
