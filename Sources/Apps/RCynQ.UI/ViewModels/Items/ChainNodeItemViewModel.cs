using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.Api;
using RCynQ.Core.State;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ChainNodeItemViewModel"/> class.
    /// </summary>
    /// <param name="link">The link.</param>
    public ChainNodeItemViewModel(LinkDto link)
    {
        ArgumentNullException.ThrowIfNull(link);
        LinkId = link.Id;
        Name = link.PeerNodeId.Value.ToUpperInvariant();
        Role = link.PeerRole.ToString();
        Address = link.Host;
    }

    /// <summary>Gets the link identifier.</summary>
    public Guid LinkId { get; }

    /// <summary>Gets the node name.</summary>
    public string Name { get; }

    /// <summary>Gets the node role.</summary>
    public string Role { get; }

    /// <summary>Gets the node address.</summary>
    public string Address { get; }

    /// <summary>Gets the status label.</summary>
    public string StatusText => DisplayText.Link(Status);

    /// <summary>Gets a value indicating whether the node is online.</summary>
    public bool IsOnline => Status == LinkStatusKind.Online;

    /// <summary>Gets a value indicating whether the node is reachable through a fallback.</summary>
    public bool IsDegraded => Status == LinkStatusKind.Degraded;

    /// <summary>Gets a value indicating whether the node is offline.</summary>
    public bool IsOffline => Status == LinkStatusKind.Offline;

    /// <summary>
    /// Tells the bindings that the texts of the row changed with the language.
    /// </summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);

    /// <summary>
    /// Applies a link state.
    /// </summary>
    /// <param name="state">The state.</param>
    public void ApplyState(LinkState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Status = DisplayText.LinkKind(state.Status);
    }
}
