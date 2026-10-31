using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Repositories;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The branches tab: current branch, divergence from Origin, pull and applying a patch file.
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
    }

    /// <inheritdoc />
    protected override Task<bool> LoadAsync() => RunQuietAsync(async () => Apply(await _api.GetBranchAsync()));

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
    }

    /// <summary>
    /// Fetches from Origin and fast-forwards the current branch.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task PullAsync() => RunSafeAsync(async () => Show(await _api.PullAsync(), "Branch.PullSucceeded", "Branch.PullFailed"));

    /// <summary>
    /// Fetches from Origin without changing the working tree.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task FetchAsync() => RunSafeAsync(async () => Show(await _api.FetchAsync(), "Branch.FetchSucceeded", "Branch.FetchFailed"));

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
}
