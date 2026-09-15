using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.State;
using RCynQ.UI.Services.Api;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The "Ветки" tab: current branch, divergence from Dest and pull.
/// </summary>
public sealed partial class BranchViewModel : PageViewModelBase
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The current branch.</summary>
    [ObservableProperty]
    private string _currentBranch = "—";

    /// <summary>The divergence description.</summary>
    [ObservableProperty]
    private string _divergenceText = string.Empty;

    /// <summary>Whether the node may fetch and pull from Dest.</summary>
    [ObservableProperty]
    private bool _canPull;

    /// <summary>The output of the last Git operation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutput))]
    private string _output = string.Empty;

    /// <summary>Gets a value indicating whether Git output is shown.</summary>
    public bool HasOutput => Output.Length > 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="BranchViewModel"/> class.
    /// </summary>
    /// <param name="api">The API client.</param>
    public BranchViewModel(IServiceApiClient api)
    {
        _api = api;
    }

    /// <summary>
    /// Adapts the page to the role capabilities.
    /// </summary>
    /// <param name="capabilities">The capabilities.</param>
    public void Configure(RoleCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        CanPull = capabilities.FetchesGit;
    }

    /// <inheritdoc />
    public override Task RefreshAsync() => RunSafeAsync(async () => Apply(await _api.GetBranchAsync()));

    /// <summary>
    /// Applies branch information.
    /// </summary>
    /// <param name="info">The branch information.</param>
    public void Apply(GitBranchInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!info.IsRepository)
        {
            CurrentBranch = "не Git-репозиторий";
            DivergenceText = string.Empty;
            return;
        }

        CurrentBranch = info.Branch ?? "(detached HEAD)";
        DivergenceText = info.TrackingRef is null
            ? CanPull ? "ветка ещё не получена с DEST — выполните Pull" : string.Empty
            : $"расхождение с DEST: {info.Behind} коммитов (локально впереди: {info.Ahead})";
    }

    /// <summary>
    /// Fetches from Dest and fast-forwards the current branch.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task PullAsync() => RunSafeAsync(async () =>
    {
        var result = await _api.PullAsync();
        Output = result.Output;
        if (result.Branch is not null)
        {
            Apply(result.Branch);
        }

        if (!result.Succeeded)
        {
            ErrorMessage = "Pull не выполнен, см. вывод.";
        }
    });

    /// <summary>
    /// Fetches from Dest without changing the working tree.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task FetchAsync() => RunSafeAsync(async () =>
    {
        var result = await _api.FetchAsync();
        Output = result.Output;
        if (result.Branch is not null)
        {
            Apply(result.Branch);
        }
    });
}
