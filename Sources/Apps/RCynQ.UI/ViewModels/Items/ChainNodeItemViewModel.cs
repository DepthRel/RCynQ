using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.Api;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.UI.Enums;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A row of the chain nodes table on the "Роль и узлы" tab.
/// </summary>
public sealed partial class ChainNodeItemViewModel : ObservableObject
{
    /// <summary>The visual status.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsOnline), nameof(IsDegraded), nameof(IsOffline))]
    private LinkStatusKind _status = LinkStatusKind.Waiting;

    /// <summary>The address of the node in use.</summary>
    [ObservableProperty]
    private string _address;

    /// <summary>Whether the node chooses the route itself.</summary>
    [ObservableProperty]
    private bool _isAutoRoute = true;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChainNodeItemViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="link">The link.</param>
    public ChainNodeItemViewModel(ILocalizer localizer, LinkDto link)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        ArgumentNullException.ThrowIfNull(link);
        LinkId = link.Id;
        Name = link.PeerNodeId.Value.ToUpperInvariant();
        Role = link.PeerRole.ToString();
        _address = link.Host;
        Routes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRoutes));
    }

    /// <summary>Gets the link identifier.</summary>
    public Guid LinkId { get; }

    /// <summary>Gets the node name.</summary>
    public string Name { get; }

    /// <summary>Gets the node role.</summary>
    public string Role { get; }

    /// <summary>Gets the routes to the node; empty when it has the one address of its link.</summary>
    public ObservableCollection<LinkRouteItemViewModel> Routes { get; } = new();

    /// <summary>Gets a value indicating whether the node is reachable in more than one way.</summary>
    public bool HasRoutes => Routes.Count > 0;

    /// <summary>Gets the status label.</summary>
    public string StatusText => DisplayText.Link(_localizer, Status);

    /// <summary>Gets a value indicating whether the node is online.</summary>
    public bool IsOnline => Status == LinkStatusKind.Online;

    /// <summary>Gets a value indicating whether the node is reachable through a fallback.</summary>
    public bool IsDegraded => Status == LinkStatusKind.Degraded;

    /// <summary>Gets a value indicating whether the node is offline.</summary>
    public bool IsOffline => Status == LinkStatusKind.Offline;

    /// <summary>
    /// Tells the bindings that the texts of the row changed with the language.
    /// </summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(string.Empty);

        // The rows of the routes word their state themselves and are replaced to word it again.
        var routes = Routes.ToArray();
        Routes.Clear();
        foreach (var route in routes)
        {
            Routes.Add(route);
        }
    }

    /// <summary>
    /// Applies a link state.
    /// </summary>
    /// <param name="state">The state.</param>
    public void ApplyState(LinkState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Status = DisplayText.LinkKind(state.Status);
    }

    /// <summary>
    /// Shows the routes of the link as the service reported them.
    /// </summary>
    /// <param name="routes">The routes, or <see langword="null"/> when the link has none.</param>
    public void ApplyRoutes(LinkRoutesDto? routes)
    {
        Routes.Clear();
        IsAutoRoute = routes?.PinnedRouteId is null;
        if (routes is null)
        {
            return;
        }

        foreach (var route in routes.Routes)
        {
            Routes.Add(new LinkRouteItemViewModel(_localizer, LinkId, route, route.Id == routes.PinnedRouteId));
            if (route.IsActive)
            {
                Address = route.Host;
            }
        }
    }
}
