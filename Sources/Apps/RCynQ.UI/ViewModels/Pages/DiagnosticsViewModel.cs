using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Events;
using RCynQ.Localization;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Formatting;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The diagnostics tab: runs checks and applies confirmed safe fixes.
/// </summary>
public sealed partial class DiagnosticsViewModel : PageViewModelBase
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>The last run, or <see langword="null"/> when the check has not been run yet.</summary>
    private DiagnosticsEvent? _lastRun;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    public DiagnosticsViewModel(ILocalizer localizer, IServiceApiClient api, IDialogService dialogs)
        : base(localizer)
    {
        _api = api;
        _dialogs = dialogs;
        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoResults));
    }

    /// <summary>Gets the check results.</summary>
    public ObservableCollection<DiagnosticItemViewModel> Results { get; } = new();

    /// <summary>Gets the summary of the last run in the language of the user.</summary>
    public string Summary => _lastRun is not { } run
        ? Localizer.Get("Diagnostics.NeverRun")
        : Localizer.Format("Diagnostics.Summary", DisplayText.Time(Localizer, run.TimestampUtc), run.Results.Count(result => result.Passed), run.Results.Count);

    /// <summary>Gets a value indicating whether no results are shown.</summary>
    public bool HasNoResults => Results.Count == 0;

    /// <inheritdoc />
    protected override Task<bool> LoadAsync() => Task.FromResult(true);

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

        _lastRun = run;
        OnPropertyChanged(nameof(Summary));
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
        if (item?.Result.AutoFix is not { } fix || !await _dialogs.ConfirmAsync(Localizer.Get("Diagnostics.FixTitle"), fix.Description))
        {
            return;
        }

        if (await RunSafeAsync(async () => InfoMessage = (await _api.ApplyFixAsync(fix, confirmed: true)).Message))
        {
            NotifyDataChanged();
            await RunAsync();
        }
    }
}
