using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Events;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Formatting;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The "Диагностика" tab: runs checks and applies confirmed safe fixes.
/// </summary>
public sealed partial class DiagnosticsViewModel : PageViewModelBase
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>The summary of the last run.</summary>
    [ObservableProperty]
    private string _summary = "Проверка ещё не запускалась.";

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsViewModel"/> class.
    /// </summary>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    public DiagnosticsViewModel(IServiceApiClient api, IDialogService dialogs)
    {
        _api = api;
        _dialogs = dialogs;
        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoResults));
    }

    /// <summary>Gets the check results.</summary>
    public ObservableCollection<DiagnosticItemViewModel> Results { get; } = new();

    /// <summary>Gets a value indicating whether no results are shown.</summary>
    public bool HasNoResults => Results.Count == 0;

    /// <inheritdoc />
    public override Task RefreshAsync() => Task.CompletedTask;

    /// <summary>
    /// Applies a diagnostics run.
    /// </summary>
    /// <param name="run">The run.</param>
    public void ApplyDiagnostics(DiagnosticsEvent run)
    {
        ArgumentNullException.ThrowIfNull(run);
        Results.Clear();
        foreach (var result in run.Results)
        {
            Results.Add(new DiagnosticItemViewModel(result));
        }

        var failed = run.Results.Count(result => !result.Passed);
        Summary = $"{DisplayText.Time(run.TimestampUtc)}: пройдено {run.Results.Count - failed} из {run.Results.Count}.";
    }

    /// <summary>
    /// Runs diagnostics.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task RunAsync() => RunSafeAsync(async () => ApplyDiagnostics(await _api.RunDiagnosticsAsync()));

    /// <summary>
    /// Applies the automatic fix of a failed check after confirmation.
    /// </summary>
    /// <param name="item">The check.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task FixAsync(DiagnosticItemViewModel? item)
    {
        if (item?.Result.AutoFix is not { } fix || !await _dialogs.ConfirmAsync("Исправить автоматически", fix.Description))
        {
            return;
        }

        if (await RunSafeAsync(async () => InfoMessage = (await _api.ApplyFixAsync(fix, confirmed: true)).Message))
        {
            await RunAsync();
        }
    }
}
