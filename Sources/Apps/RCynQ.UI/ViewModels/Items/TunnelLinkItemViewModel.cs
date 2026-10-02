using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Localization;
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
    [NotifyPropertyChangedFor(nameof(ActiveTransportText))]
    private TransportKind _activeTransport;

    /// <summary>The last status message.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    /// <summary>The configured fallback transports.</summary>
    private readonly IReadOnlyList<TransportKind> _fallbackTransports;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunnelLinkItemViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="link">The link.</param>
    public TunnelLinkItemViewModel(ILocalizer localizer, LinkDto link)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        ArgumentNullException.ThrowIfNull(link);
        LinkId = link.Id;
        Name = link.Name;
        PeerText = $"{link.PeerNodeId} · {link.PeerRole}";
        Address = $"{link.SshUser}@{link.Host}:{link.SshPort}";
        Transport = link.Transport;
        _activeTransport = link.Transport;
        _fallbackTransports = [.. link.FallbackTransports];
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
    public string FallbackText => _fallbackTransports.Count == 0
        ? _localizer.Get("Tunnel.NoFallback")
        : _localizer.Format("Tunnel.Fallback", string.Join(", ", _fallbackTransports.Select(kind => DisplayText.Transport(_localizer, kind))));

    /// <summary>Gets the label of the active transport.</summary>
    public string ActiveTransportText => DisplayText.Transport(_localizer, ActiveTransport);

    /// <summary>Gets a value indicating whether the tunnel can be started or stopped (not for Direct).</summary>
    public bool CanToggle => Transport != TransportKind.Direct;

    /// <summary>Gets the status label.</summary>
    public string StatusText => DisplayText.Link(_localizer, Status);

    /// <summary>Gets a value indicating whether the link is online.</summary>
    public bool IsOnline => Status == LinkStatusKind.Online;

    /// <summary>Gets a value indicating whether the link runs on a fallback.</summary>
    public bool IsDegraded => Status == LinkStatusKind.Degraded;

    /// <summary>Gets a value indicating whether the link is offline.</summary>
    public bool IsOffline => Status == LinkStatusKind.Offline;

    /// <summary>Gets a value indicating whether there is a status message to show under the row.</summary>
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

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
        ActiveTransport = state.Kind;
        Message = state.Message;
    }
}
