using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.State;
using RCynQ.UI.Services.Api;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The "Туннель" tab: chain links, their transports and state, start and stop.
/// </summary>
public sealed partial class TunnelViewModel : PageViewModelBase
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunnelViewModel"/> class.
    /// </summary>
    /// <param name="api">The API client.</param>
    public TunnelViewModel(IServiceApiClient api)
    {
        _api = api;
        Links.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoLinks));
    }

    /// <summary>Gets the links.</summary>
    public ObservableCollection<TunnelLinkItemViewModel> Links { get; } = new();

    /// <summary>Gets a value indicating whether no links are configured.</summary>
    public bool HasNoLinks => Links.Count == 0;

    /// <inheritdoc />
    public override Task RefreshAsync() => RunSafeAsync(async () =>
    {
        var links = await _api.GetLinksAsync();
        Links.Clear();
        foreach (var link in links)
        {
            Links.Add(new TunnelLinkItemViewModel(link));
        }

        foreach (var state in await _api.GetLinkStatusAsync())
        {
            ApplyLinkState(state);
        }
    });

    /// <summary>
    /// Applies a link state pushed by the service.
    /// </summary>
    /// <param name="state">The state.</param>
    public void ApplyLinkState(LinkState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Links.FirstOrDefault(link => link.LinkId == state.LinkId)?.ApplyState(state);
    }

    /// <summary>
    /// Starts a tunnel.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task StartAsync(TunnelLinkItemViewModel? link)
        => link is null ? Task.CompletedTask : RunSafeAsync(async () => link.ApplyState(await _api.StartLinkAsync(link.LinkId)));

    /// <summary>
    /// Stops a tunnel.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task StopAsync(TunnelLinkItemViewModel? link)
        => link is null ? Task.CompletedTask : RunSafeAsync(async () => link.ApplyState(await _api.StopLinkAsync(link.LinkId)));
}
