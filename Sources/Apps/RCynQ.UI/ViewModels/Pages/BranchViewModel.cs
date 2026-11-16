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
using RCynQ.UI.ViewModels.Items;
using RCynQ.UI.ViewModels.Repositories;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The branches tab: current branch, divergence from Origin, switching to another branch, pull and applying a patch file.
/// </summary>
public sealed partial class BranchViewModel : PageViewModelBase
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>The store of the interface settings, or <see langword="null"/> when the choices are not kept.</summary>
    private readonly IUiSettingsStore? _uiSettings;

    /// <summary>The last branch information, or <see langword="null"/> before the first reply of the service.</summary>
    private GitBranchInfo? _branch;

    /// <summary>Whether the node may fetch and pull from Origin.</summary>
    [ObservableProperty]
    private bool _canPull;

    /// <summary>The output of the last Git operation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutput))]
    private string _output = string.Empty;

    /// <summary>The text the names of the branches in the switcher contain.</summary>
    [ObservableProperty]
    private string _branchFilter = string.Empty;

    /// <summary>The branch the Origin had checked out at the last Fetch, or <see langword="null"/> when it is not known.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OriginHeadWarning))]
    [NotifyPropertyChangedFor(nameof(HasOriginHeadWarning))]
    private string? _originHead;

    /// <summary>Every branch the working tree can be switched to, as the service listed them.</summary>
    private IReadOnlyList<SwitchBranchItemViewModel> _switchBranches = Array.Empty<SwitchBranchItemViewModel>();

    /// <summary>Gets the filter of the switcher by the state of a branch: here or not, on the Origin or only local.</summary>
    public BranchStatusFilterViewModel StatusFilter { get; } = new();

    /// <summary>Gets the branches of the switcher that match <see cref="BranchFilter"/> and <see cref="StatusFilter"/>.</summary>
    public ObservableCollection<SwitchBranchItemViewModel> FilteredBranches { get; } = new();

    /// <summary>Gets a value indicating whether the branch switcher is shown: a Workstation with branches to choose from.</summary>
    public bool CanSwitch => CanPull && _switchBranches.Count > 0;

    /// <summary>Gets a value indicating whether the filter of the switcher leaves no branch.</summary>
    public bool HasNoFilteredBranches => FilteredBranches.Count == 0;

    /// <summary>
    /// Gets the warning that the Origin has another branch checked out than the working tree, so changes sent now land there.
    /// </summary>
    public string OriginHeadWarning => HasOriginHeadWarning ? Localizer.Format("Branch.OriginHeadDiffers", OriginHead!) : string.Empty;

    /// <summary>Gets a value indicating whether <see cref="OriginHeadWarning"/> is shown.</summary>
    public bool HasOriginHeadWarning => CanPull && OriginHead is not null && _branch?.Branch is { } current && current != OriginHead;

    /// <summary>Gets the current branch.</summary>
    public string CurrentBranch => _branch switch
    {
        null => "—",
        { IsRepository: false } => Localizer.Get("Branch.NotRepository"),
        var info => info.Branch ?? "(detached HEAD)"
    };

    /// <summary>Gets the divergence description in the language of the user.</summary>
    public string DivergenceText => _branch switch
    {
        null or { IsRepository: false } => string.Empty,
        { TrackingRef: null } => CanPull ? Localizer.Get("Branch.NotFetched") : string.Empty,
        var info => Localizer.Format("Branch.Divergence", info.Behind, info.Ahead)
    };

    /// <summary>
    /// Gets or sets the repositories of the node, listed on the page; <see langword="null"/> shows no list.
    /// </summary>
    public RepositoriesViewModel? Repositories { get; set; }

    /// <summary>Gets a value indicating whether Git output is shown.</summary>
    public bool HasOutput => Output.Length > 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="BranchViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    /// <param name="uiSettings">The store that keeps how lists of files are laid out, or <see langword="null"/>.</param>
    public BranchViewModel(ILocalizer localizer, IServiceApiClient api, IDialogService dialogs, IUiSettingsStore? uiSettings = null)
        : base(localizer)
    {
        _api = api;
        _dialogs = dialogs;
        _uiSettings = uiSettings;
        StatusFilter.Changed += (_, _) => FilterBranches();
    }

    /// <summary>
    /// Adapts the page to the role capabilities.
    /// </summary>
    /// <param name="capabilities">The capabilities.</param>
    public void Configure(RoleCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        CanPull = capabilities.FetchesGit;
        OnPropertyChanged(nameof(DivergenceText));
        OnPropertyChanged(nameof(CanSwitch));
        OnPropertyChanged(nameof(HasOriginHeadWarning));
        OnPropertyChanged(nameof(OriginHeadWarning));
    }

    /// <inheritdoc />
    protected override Task<bool> LoadAsync() => RunQuietAsync(async () =>
    {
        Apply(await _api.GetBranchAsync());
        if (CanPull && await _api.GetSwitchableBranchesAsync() is { } branches)
        {
            ApplyBranches(branches);
        }
    });

    /// <summary>
    /// Applies branch information.
    /// </summary>
    /// <param name="info">The branch information.</param>
    public void Apply(GitBranchInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        _branch = info;
        OnPropertyChanged(nameof(CurrentBranch));
        OnPropertyChanged(nameof(DivergenceText));
        OnPropertyChanged(nameof(HasOriginHeadWarning));
        OnPropertyChanged(nameof(OriginHeadWarning));
    }

    /// <summary>
    /// Applies the branches the working tree can be switched to.
    /// </summary>
    /// <param name="branches">The branches, as the service listed them.</param>
    public void ApplyBranches(GitSwitchBranchesResponse branches)
    {
        ArgumentNullException.ThrowIfNull(branches);
        _switchBranches = branches.Branches
            .Select(branch => new SwitchBranchItemViewModel(Localizer, branch, branch.Name == branches.Current))
            .ToArray();
        OriginHead = branches.OriginHead;
        OnPropertyChanged(nameof(CanSwitch));
        FilterBranches();
    }

    /// <summary>
    /// Fetches from Origin and fast-forwards the current branch.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task PullAsync()
    {
        await RunSafeAsync(async () => Show(await _api.PullAsync(), "Branch.PullSucceeded", "Branch.PullFailed"));
        await ReloadBranchesQuietlyAsync();
    }

    /// <summary>
    /// Fetches from Origin without changing the working tree.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task FetchAsync()
    {
        await RunSafeAsync(async () => Show(await _api.FetchAsync(), "Branch.FetchSucceeded", "Branch.FetchFailed"));
        await ReloadBranchesQuietlyAsync();
    }

    /// <summary>
    /// Switches the working tree to a branch. When the working tree has changes, the user chooses first whether they are
    /// carried over or saved to a patch file; nothing is ever thrown away. Changes saved to a file are offered back right
    /// after the switch, from that same file.
    /// </summary>
    /// <param name="item">The branch chosen in the switcher.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task SwitchToAsync(SwitchBranchItemViewModel? item)
    {
        if (item is null || item.IsCurrent)
        {
            return;
        }

        SavedChanges? saved = null;
        SwitchBranchResult? result = null;
        await RunSafeAsync(async () =>
        {
            result = await _api.SwitchBranchAsync(new SwitchBranchRequest(item.Name, BranchSwitchMode.IfClean));
            if (result.Outcome != BranchSwitchOutcome.ChangesInTheWay)
            {
                return;
            }

            var dialog = new SwitchBranchDialogViewModel(Localizer, item.Name, result.Files, () => PickSwitchPatchAsync(item.Name));
            var choice = await _dialogs.ShowSwitchBranchDialogAsync(dialog);
            if (choice == BranchSwitchMode.CarryChanges)
            {
                result = await _api.SwitchBranchAsync(new SwitchBranchRequest(item.Name, BranchSwitchMode.CarryChanges));
            }
            else if (choice == BranchSwitchMode.SavePatch && dialog.PatchPath is { } file)
            {
                saved = await SaveChangesAsync(file);
                result = await _api.SwitchBranchAsync(saved is null
                    ? new SwitchBranchRequest(item.Name, BranchSwitchMode.IfClean)
                    : new SwitchBranchRequest(item.Name, BranchSwitchMode.SavePatch, saved.Patch));
            }
            else
            {
                result = null;
            }
        });

        if (result is not null)
        {
            ShowSwitch(result, saved?.File);
            NotifyDataChanged();
            if (result.Outcome == BranchSwitchOutcome.Switched && saved is not null)
            {
                await OfferRestoreAsync(result.Branch?.Branch ?? item.Name, result.Message, saved);
            }
        }

        await ReloadBranchesQuietlyAsync();
    }

    /// <summary>
    /// Asks where to save the changes before a switch; the switch dialog stays open under the picker.
    /// </summary>
    /// <param name="branch">The branch the user switches to; it names the suggested file.</param>
    /// <returns>The chosen file, or <see langword="null"/> when the user gave up.</returns>
    private Task<string?> PickSwitchPatchAsync(string branch)
        => _dialogs.PickPatchToSaveAsync(Localizer.Get("BranchSwitch.SaveTitle"), $"{branch.Replace('/', '-')}-changes.patch", Localizer.Get("PatchFile.FileType"));

    /// <summary>
    /// Writes the uncommitted changes to the patch file the user chose; the service takes them out of the working tree
    /// only after the file is written.
    /// </summary>
    /// <param name="file">The chosen file.</param>
    /// <returns>The saved changes, or <see langword="null"/> when there was nothing to save any more.</returns>
    private async Task<SavedChanges?> SaveChangesAsync(string file)
    {
        var patch = await _api.BuildSwitchPatchAsync();
        if (patch.Length == 0)
        {
            // The changes went away meanwhile: nothing to save, so the switch only needs a clean working tree.
            return null;
        }

        await File.WriteAllBytesAsync(file, patch);
        return new SavedChanges(file, patch);
    }

    /// <summary>
    /// Offers to bring the changes saved before the switch back into the working tree, from the file they were saved to.
    /// When Git refuses, nothing changes and the file stays where it is, so the changes are never lost.
    /// </summary>
    /// <param name="branch">The branch the working tree is on now.</param>
    /// <param name="switchMessage">The message of the switch.</param>
    /// <param name="saved">The saved changes.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task OfferRestoreAsync(string branch, string switchMessage, SavedChanges saved)
    {
        if (!await _dialogs.ShowRestoreChangesDialogAsync(new RestoreChangesDialogViewModel(Localizer, branch, saved.File)))
        {
            return;
        }

        PatchFileApplyResult? restored = null;
        var answered = await RunSafeAsync(async () => restored = await _api.ApplyPatchFileAsync(saved.Patch, null, reverse: false));
        NotifyDataChanged();

        if (restored is { Succeeded: true })
        {
            Output = restored.Output;
            SuccessMessage = $"{switchMessage}\n{Localizer.Format("BranchSwitch.Restored", saved.File)}";
            return;
        }

        // The switch did happen: say so first, then why the changes are still only in the file.
        var reason = answered ? restored?.Message : ErrorMessage;
        var lines = new[] { switchMessage, reason, Localizer.Format("BranchSwitch.RestoreFailed", saved.File) };
        Output = restored?.Output ?? string.Empty;
        ErrorMessage = string.Join('\n', lines.Where(line => !string.IsNullOrEmpty(line)));
    }

    /// <summary>
    /// Shows how a switch ended: the branch after it and a green or red message, with the file the changes were saved to.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="savedTo">The patch file with the saved changes, or <see langword="null"/>.</param>
    private void ShowSwitch(SwitchBranchResult result, string? savedTo)
    {
        if (result.Branch is not null)
        {
            Apply(result.Branch);
        }

        var message = savedTo is null ? result.Message : $"{result.Message}\n{Localizer.Format("BranchSwitch.SavedTo", savedTo)}";
        if (result.Outcome == BranchSwitchOutcome.Switched)
        {
            SuccessMessage = message;
        }
        else
        {
            ErrorMessage = message;
        }
    }

    /// <summary>
    /// Reloads the branches of the switcher; a failure leaves the list as it was.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    private async Task ReloadBranchesQuietlyAsync()
    {
        if (!CanPull)
        {
            return;
        }

        try
        {
            if (await _api.GetSwitchableBranchesAsync() is { } branches)
            {
                ApplyBranches(branches);
            }
        }
        catch (Exception ex) when (ex is ServiceApiException or HttpRequestException or TaskCanceledException)
        {
            // The next load of the page asks again.
        }
    }

    /// <summary>
    /// Shows the branches whose names contain the search text and whose state passes the status filter.
    /// </summary>
    private void FilterBranches()
    {
        FilteredBranches.Clear();
        foreach (var branch in _switchBranches.Where(branch => branch.Matches(BranchFilter) && StatusFilter.Matches(branch)))
        {
            FilteredBranches.Add(branch);
        }

        OnPropertyChanged(nameof(HasNoFilteredBranches));
    }

    /// <summary>
    /// Filters the branches of the switcher as the user types.
    /// </summary>
    /// <param name="value">The new filter.</param>
    partial void OnBranchFilterChanged(string value) => FilterBranches();

    /// <summary>
    /// Asks for a patch file and its files, and brings the chosen ones into the working branch without committing them.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task ApplyPatchAsync()
    {
        var file = await _dialogs.PickPatchToOpenAsync(Localizer.Get("ApplyPatch.OpenTitle"), Localizer.Get("PatchFile.FileType"));
        if (file is null)
        {
            return;
        }

        await RunSafeAsync(async () =>
        {
            if (new FileInfo(file).Length > ApiLimits.MAX_PATCH_FILE_BYTES)
            {
                ErrorMessage = Localizer.Format("PatchFile.TooLarge", ApiLimits.MAX_PATCH_FILE_BYTES / (1024 * 1024));
                return;
            }

            var patch = await File.ReadAllBytesAsync(file);
            var files = await _api.ReadPatchFileAsync(patch);
            var dialog = new ApplyPatchDialogViewModel(Localizer, Path.GetFileName(file), files, _uiSettings);
            if (!await _dialogs.ShowApplyPatchDialogAsync(dialog))
            {
                return;
            }

            var result = await _api.ApplyPatchFileAsync(patch, dialog.ChosenPaths, reverse: false);
            Output = result.Output;
            NotifyDataChanged();
            if (result.Succeeded)
            {
                SuccessMessage = result.Message;
            }
            else
            {
                ErrorMessage = result.Message;
            }
        });
    }

    /// <summary>
    /// Shows the result of a Git operation: its output, the branch after it and a green or red message.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="successKey">The key of the message about a success.</param>
    /// <param name="failureKey">The key of the message about a failure.</param>
    private void Show(GitOperationResult result, string successKey, string failureKey)
    {
        Output = result.Output;
        NotifyDataChanged();
        if (result.Branch is not null)
        {
            Apply(result.Branch);
        }

        if (result.Succeeded)
        {
            SuccessMessage = Localizer.Get(successKey);
        }
        else
        {
            ErrorMessage = Localizer.Get(failureKey);
        }
    }

    /// <summary>
    /// The uncommitted changes saved to a patch file before a switch: where they went and what was written there.
    /// </summary>
    /// <param name="File">The patch file the user chose.</param>
    /// <param name="Patch">The bytes written to it; the same bytes bring the changes back.</param>
    private sealed record SavedChanges(string File, byte[] Patch);
}
