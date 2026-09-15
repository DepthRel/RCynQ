using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A link on the "Туннель" and "Обзор" tabs.
/// </summary>
public sealed partial class TunnelLinkItemViewModel : ObservableObject
{
    /// <summary>The visual status.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsOnline), nameof(IsDegraded), nameof(IsOffline))]
    private LinkStatusKind _status = LinkStatusKind.Waiting;

    /// <summary>The transport currently in use.</summary>
    [ObservableProperty]
    private TransportKind _activeTransport;

    /// <summary>The last status message.</summary>
    [ObservableProperty]
    private string? _message;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunnelLinkItemViewModel"/> class.
    /// </summary>
    /// <param name="link">The link.</param>
    public TunnelLinkItemViewModel(LinkDto link)
    {
        ArgumentNullException.ThrowIfNull(link);
        LinkId = link.Id;
        Name = link.Name;
        PeerText = $"{link.PeerNodeId} · {link.PeerRole}";
        Address = $"{link.SshUser}@{link.Host}:{link.SshPort}";
        Transport = link.Transport;
        _activeTransport = link.Transport;
        FallbackText = link.FallbackTransports.Count == 0 ? "без резерва" : "резерв: " + string.Join(", ", link.FallbackTransports);
    }

    /// <summary>Gets the link identifier.</summary>
    public Guid LinkId { get; }

    /// <summary>Gets the link name.</summary>
    public string Name { get; }

    /// <summary>Gets the peer description.</summary>
    public string PeerText { get; }

    /// <summary>Gets the SSH address.</summary>
    public string Address { get; }

    /// <summary>Gets the configured transport.</summary>
    public TransportKind Transport { get; }

    /// <summary>Gets the fallback description.</summary>
    public string FallbackText { get; }

    /// <summary>Gets a value indicating whether the tunnel can be started or stopped (not for Direct).</summary>
    public bool CanToggle => Transport != TransportKind.Direct;

    /// <summary>Gets the status label.</summary>
    public string StatusText => DisplayText.Link(Status);

    /// <summary>Gets a value indicating whether the link is online.</summary>
    public bool IsOnline => Status == LinkStatusKind.Online;

    /// <summary>Gets a value indicating whether the link runs on a fallback.</summary>
    public bool IsDegraded => Status == LinkStatusKind.Degraded;

    /// <summary>Gets a value indicating whether the link is offline.</summary>
    public bool IsOffline => Status == LinkStatusKind.Offline;

    /// <summary>
    /// Applies a link state.
    /// </summary>
    /// <param name="state">The state.</param>
    public void ApplyState(LinkState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Status = DisplayText.LinkKind(state.Status);
        ActiveTransport = state.Kind;
        Message = state.Message;
    }
}
