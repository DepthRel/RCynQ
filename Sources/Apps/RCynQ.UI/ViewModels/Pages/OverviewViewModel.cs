using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.ViewModels.Formatting;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The overview tab: role, last synchronization, link statuses and problems with quick navigation.
/// </summary>
public sealed partial class OverviewViewModel : PageViewModelBase
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The failed checks of the last diagnostics run.</summary>
    private readonly List<string> _diagnosticFailures = new();

    /// <summary>The last synchronization status.</summary>
    private NodeSyncStatus? _status;

    /// <summary>The node configuration, or <see langword="null"/> until it is known.</summary>
    private NodeConfigurationDto? _configuration;

    /// <summary>The node role.</summary>
    [ObservableProperty]
    private string _roleText = "—";

    /// <summary>The node name.</summary>
    [ObservableProperty]
    private string _nodeName = "—";

    /// <summary>The last synchronization time, or <see langword="null"/> when there was none.</summary>
    private DateTimeOffset? _lastSync;

    /// <summary>Whether problems exist.</summary>
    [ObservableProperty]
    private bool _hasProblems;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverviewViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="api">The API client.</param>
    public OverviewViewModel(ILocalizer localizer, IServiceApiClient api)
        : base(localizer)
    {
        _api = api;
        Links.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoLinks));
    }

    /// <summary>Gets the last synchronization time in the language of the user.</summary>
    public string LastSyncText => DisplayText.Time(Localizer, _lastSync);

    /// <summary>Gets a value indicating whether no links are configured.</summary>
    public bool HasNoLinks => Links.Count == 0;

    /// <summary>
    /// Raised when the user jumps to the tab of a problem.
    /// </summary>
    public event EventHandler<TabKey>? NavigationRequested;

    /// <summary>Gets the links.</summary>
    public ObservableCollection<TunnelLinkItemViewModel> Links { get; } = new();

    /// <summary>Gets the problems.</summary>
    public ObservableCollection<ProblemItemViewModel> Problems { get; } = new();

    /// <summary>
    /// Takes the node configuration the main window has already read, so the page does not ask for it again.
    /// </summary>
    /// <param name="configuration">The node configuration.</param>
    public void Configure(NodeConfigurationDto configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        RoleText = configuration.Role.ToString();
        NodeName = $"{configuration.Name} ({configuration.NodeId})";
    }

    /// <inheritdoc />
    protected override Task<bool> LoadAsync() => RunQuietAsync(async () =>
    {
        // The requests do not depend on each other; the status is the light one, without a walk over the working tree.
        var configuration = _configuration is null ? _api.GetConfigAsync() : Task.FromResult(_configuration);
        var summary = _api.GetStateSummaryAsync();
        var configured = _api.GetLinksAsync();
        var states = _api.GetLinkStatusAsync();
        await Task.WhenAll(configuration, summary, configured, states);

        Configure(await configuration);
        _status = await summary;
        var last = new[] { _status.State.LastSentAtUtc, _status.State.LastReceivedAtUtc }.Max();
        _lastSync = last;
        OnPropertyChanged(nameof(LastSyncText));

        var links = await configured;
        Links.Clear();
        foreach (var link in links)
        {
            Links.Add(new TunnelLinkItemViewModel(Localizer, link));
        }

        foreach (var state in await states)
        {
            ApplyLinkState(state);
        }

        RebuildProblems();
    });

    /// <inheritdoc />
    public override void RefreshTexts()
    {
        base.RefreshTexts();
        foreach (var link in Links)
        {
            link.RefreshTexts();
        }

        RebuildProblems();
    }

    /// <summary>
    /// Applies a link state pushed by the service.
    /// </summary>
    /// <param name="state">The state.</param>
    public void ApplyLinkState(LinkState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Links.FirstOrDefault(link => link.LinkId == state.LinkId)?.ApplyState(state);
        RebuildProblems();
    }

    /// <summary>
    /// Applies a diagnostics run pushed by the service.
    /// </summary>
    /// <param name="run">The run.</param>
    public void ApplyDiagnostics(DiagnosticsEvent run)
    {
        ArgumentNullException.ThrowIfNull(run);
        _diagnosticFailures.Clear();
        _diagnosticFailures.AddRange(run.Results.Where(result => !result.Passed).Select(result => result.CheckName));
        RebuildProblems();
    }

    /// <summary>
    /// Rebuilds the problem list from the latest state, links and diagnostics.
    /// </summary>
    private void RebuildProblems()
    {
        Problems.Clear();
        if (_status is { Role: not NodeRole.Relay })
        {
            if (!_status.SyncBase.IsReady)
            {
                var problem = _status.SyncBase.Problem is { Length: > 0 } reason
                    ? Localizer.Format("Overview.Problem.NoSyncBase", reason)
                    : Localizer.Get("Overview.Problem.NoBaseline");
                Problems.Add(new ProblemItemViewModel(problem, _status.Role == NodeRole.Workstation && !_status.SyncBase.CanCreateManual ? TabKey.Branches : TabKey.Sync));
            }

            if (_status.ConflictPendingCount > 0)
            {
                Problems.Add(new ProblemItemViewModel(Localizer.Format("Overview.Problem.Conflicts", _status.ConflictPendingCount), TabKey.Sync));
            }
        }

        foreach (var link in Links.Where(link => link.IsOffline))
        {
            // The reason is what the user needs: "unavailable" alone does not say whether the path, the service of the
            // other node or its answer is the trouble.
            var text = string.IsNullOrWhiteSpace(link.Message)
                ? Localizer.Format("Overview.Problem.LinkOffline", link.Name)
                : Localizer.Format("Overview.Problem.LinkOfflineReason", link.Name, link.Message);
            Problems.Add(new ProblemItemViewModel(text, TabKey.Tunnel));
        }

        if (_diagnosticFailures.Count > 0)
        {
            Problems.Add(new ProblemItemViewModel(Localizer.Format("Overview.Problem.Diagnostics", string.Join(", ", _diagnosticFailures)), TabKey.Diagnostics));
        }

        HasProblems = Problems.Count > 0;
    }

    /// <summary>
    /// Navigates to the tab of a problem.
    /// </summary>
    /// <param name="problem">The problem.</param>
    [RelayCommand]
    private void Navigate(ProblemItemViewModel? problem)
    {
        if (problem is not null)
        {
            NavigationRequested?.Invoke(this, problem.Target);
        }
    }
}
