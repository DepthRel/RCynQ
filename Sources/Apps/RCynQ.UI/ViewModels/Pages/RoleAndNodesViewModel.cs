using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Formatting;
using RCynQ.UI.ViewModels.Items;
using RCynQ.UI.ViewModels.Setup;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The role and nodes tab: role, working path, chain nodes, export and import.
/// </summary>
public sealed partial class RoleAndNodesViewModel : PageViewModelBase
{
    /// <summary>JSON options of exported files.</summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>Runs the SSH access update with administrator rights, or <see langword="null"/>.</summary>
    private readonly IPeerAccessElevation? _elevation;

    /// <summary>The settings stored in the configuration file, or <see langword="null"/> when it is unavailable.</summary>
    private readonly ILocalNodeSettings? _settings;

    /// <summary>The loaded configuration.</summary>
    private NodeConfigurationDto? _loaded;

    /// <summary>The node identifier, shown next to the shared token.</summary>
    [ObservableProperty]
    private string _nodeId = string.Empty;

    /// <summary>The shared token of Workstation and Origin.</summary>
    [ObservableProperty]
    private string _linkToken = string.Empty;

    /// <summary>Whether the shared token is shown as text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenMaskChar), nameof(RevealTokenText))]
    private bool _isTokenRevealed;

    /// <summary>How neighbours reach this node.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEndpointPortVisible), nameof(EndpointHint))]
    private EndpointModeOption _selectedEndpointMode = null!;

    /// <summary>The TCP port of the direct endpoint.</summary>
    [ObservableProperty]
    private decimal _endpointPort = 18444;

    /// <summary>Whether the settings differ from the configuration file.</summary>
    [ObservableProperty]
    private bool _areSettingsDirty;

    /// <summary>The selected role.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PathLabel), nameof(IsPathVisible))]
    private NodeRole _selectedRole = NodeRole.Workstation;

    /// <summary>The working path.</summary>
    [ObservableProperty]
    private string _path = string.Empty;

    /// <summary>Whether the role or path differ from the running configuration.</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoleAndNodesViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    /// <param name="elevation">Runs the SSH access update with administrator rights, or <see langword="null"/> when unavailable.</param>
    /// <param name="settings">The settings kept in the configuration file (link token, endpoint mode), or <see langword="null"/> when the page must not edit them.</param>
    public RoleAndNodesViewModel(
        ILocalizer localizer,
        IServiceApiClient api,
        IDialogService dialogs,
        IPeerAccessElevation? elevation = null,
        ILocalNodeSettings? settings = null)
        : base(localizer)
    {
        // The choice made here is the state the page starts with, not an edit of the user.
        EndpointModes = EndpointModeOption.CreateAll(localizer);
        SelectedEndpointMode = EndpointModes[0];
        AreSettingsDirty = false;
        _api = api;
        _dialogs = dialogs;
        _elevation = elevation;
        _settings = settings;
        Nodes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoNodes));
    }

    /// <summary>
    /// Raised when a saved role change needs the service to restart.
    /// </summary>
    public event EventHandler? RestartRequested;

    /// <summary>Gets the selectable roles.</summary>
    public IReadOnlyList<NodeRole> Roles { get; } = new[] { NodeRole.Workstation, NodeRole.Relay, NodeRole.Origin };

    /// <summary>Gets the chain nodes.</summary>
    public ObservableCollection<ChainNodeItemViewModel> Nodes { get; } = new();

    /// <summary>Gets a value indicating whether no nodes are configured.</summary>
    public bool HasNoNodes => Nodes.Count == 0;

    /// <summary>Gets the label of the path field.</summary>
    public string PathLabel => DisplayText.PathLabel(Localizer, SelectedRole);

    /// <summary>Gets a value indicating whether the path field applies.</summary>
    public bool IsPathVisible => SelectedRole != NodeRole.Relay;

    /// <summary>Gets a value indicating whether the node settings card is shown.</summary>
    public bool AreSettingsVisible => _settings is not null;

    /// <summary>Gets a value indicating whether this node uses a shared token: a Relay never does.</summary>
    public bool HasLinkToken => SelectedRole != NodeRole.Relay;

    /// <summary>Gets the connection modes offered.</summary>
    public IReadOnlyList<EndpointModeOption> EndpointModes { get; }

    /// <summary>Gets a value indicating whether the connection mode can be chosen: a Relay always uses its SSH server.</summary>
    public bool IsEndpointModeVisible => SelectedRole != NodeRole.Relay;

    /// <summary>Gets a value indicating whether the port of the direct endpoint is shown.</summary>
    public bool IsEndpointPortVisible => IsEndpointModeVisible && SelectedEndpointMode.Value == EndpointMode.Direct;

    /// <summary>Gets the explanation of the selected connection mode.</summary>
    public string EndpointHint => SelectedEndpointMode.Hint;

    /// <summary>Gets the character hiding the token, or <c>'\0'</c> when it is revealed.</summary>
    public char TokenMaskChar => IsTokenRevealed ? '\0' : '•';

    /// <summary>Gets the caption of the reveal button.</summary>
    public string RevealTokenText => Localizer.Get(IsTokenRevealed ? "Common.Hide" : "Common.Show");

    /// <summary>Gets the path of the configuration file, shown under the settings.</summary>
    public string ConfigPath => _settings?.ConfigPath ?? string.Empty;

    /// <inheritdoc />
    protected override Task<bool> LoadAsync() => RunQuietAsync(async () =>
    {
        _loaded = await _api.GetConfigAsync();
        SelectedRole = _loaded.Role;
        Path = (_loaded.Role == NodeRole.Origin ? _loaded.RepoPath : _loaded.WorkdirPath) ?? string.Empty;
        IsDirty = false;
        LoadSettings();

        // Fetched first, then replaced in one go, so two overlapping refreshes cannot both add their rows.
        await RefreshNodesAsync();

        foreach (var state in await _api.GetLinkStatusAsync())
        {
            ApplyLinkState(state);
        }
    });

    /// <inheritdoc />
    public override void RefreshTexts()
    {
        base.RefreshTexts();
        foreach (var node in Nodes)
        {
            node.RefreshTexts();
        }
    }

    /// <summary>
    /// Applies a link state pushed by the service.
    /// </summary>
    /// <param name="state">The state.</param>
    public void ApplyLinkState(LinkState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Nodes.FirstOrDefault(node => node.LinkId == state.LinkId)?.ApplyState(state);
    }

    /// <summary>
    /// Tracks unsaved role changes.
    /// </summary>
    /// <param name="value">The new role.</param>
    partial void OnSelectedRoleChanged(NodeRole value)
    {
        UpdateDirty();
        OnPropertyChanged(nameof(HasLinkToken));
        OnPropertyChanged(nameof(IsEndpointModeVisible));
        OnPropertyChanged(nameof(IsEndpointPortVisible));
    }

    /// <summary>
    /// Marks the settings as changed when the shared link token is edited.
    /// </summary>
    /// <param name="value">The new token.</param>
    partial void OnLinkTokenChanged(string value) => AreSettingsDirty = true;

    /// <summary>
    /// Marks the settings as changed when another way of accepting connections is chosen.
    /// </summary>
    /// <param name="value">The chosen mode.</param>
    partial void OnSelectedEndpointModeChanged(EndpointModeOption value) => AreSettingsDirty = true;

    /// <summary>
    /// Marks the settings as changed when the own TLS port is edited.
    /// </summary>
    /// <param name="value">The new port.</param>
    partial void OnEndpointPortChanged(decimal value) => AreSettingsDirty = true;

    /// <summary>
    /// Reads the settings that live in the configuration file rather than in the service API.
    /// </summary>
    private void LoadSettings()
    {
        if (_settings?.Read() is not { } options)
        {
            return;
        }

        NodeId = options.NodeId;
        LinkToken = options.LinkToken ?? string.Empty;
        SelectedEndpointMode = EndpointModes.First(option => option.Value == options.Endpoint.Mode);
        EndpointPort = options.Endpoint.Port is >= 1 and <= 65535 ? options.Endpoint.Port : 18444;
        IsTokenRevealed = false;
        AreSettingsDirty = false;
    }

    /// <summary>
    /// Shows or hides the shared token.
    /// </summary>
    [RelayCommand]
    private void RevealToken() => IsTokenRevealed = !IsTokenRevealed;

    /// <summary>
    /// Copies the shared token to the clipboard.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task CopyTokenAsync()
        => InfoMessage = await _dialogs.CopyToClipboardAsync(LinkToken)
            ? Localizer.Get("Roles.TokenCopied")
            : Localizer.Get("Roles.TokenCopyFailed");

    /// <summary>
    /// Replaces the shared token with a new one after confirmation.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task NewTokenAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            Localizer.Get("Roles.NewTokenTitle"),
            Localizer.Get("Roles.NewTokenConfirm"));
        if (confirmed)
        {
            LinkToken = SetupViewModel.CreateToken();
            IsTokenRevealed = true;
            InfoMessage = Localizer.Get("Roles.NewTokenCreated");
        }
    }

    /// <summary>
    /// Writes the node settings and restarts the service.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (_settings?.Read() is not { } options)
        {
            ErrorMessage = Localizer.Get("Roles.ConfigUnavailable");
            return;
        }

        await RunSafeAsync(async () =>
        {
            var values = new NodeSetupValues(
                options.NodeId,
                options.Name,
                options.Role,
                RepositoryConfigurationEditor.ActivePath(options) ?? string.Empty,
                options.ListenPort,
                LinkToken,
                options.Role == NodeRole.Relay ? EndpointMode.SshTunnel : SelectedEndpointMode.Value,
                (int)EndpointPort,
                options.Endpoint.BindAddress);
            var errors = await _settings.SaveAsync(values);
            if (errors.Count > 0)
            {
                ErrorMessage = string.Join(Environment.NewLine, errors);
                return;
            }

            AreSettingsDirty = false;
            InfoMessage = Localizer.Get("Roles.SettingsSaved");
            RestartRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Tracks unsaved path changes.
    /// </summary>
    /// <param name="value">The new path.</param>
    partial void OnPathChanged(string value) => UpdateDirty();

    /// <summary>
    /// Recomputes <see cref="IsDirty"/>.
    /// </summary>
    private void UpdateDirty()
    {
        if (_loaded is null)
        {
            return;
        }

        var loadedPath = (_loaded.Role == NodeRole.Origin ? _loaded.RepoPath : _loaded.WorkdirPath) ?? string.Empty;
        IsDirty = SelectedRole != _loaded.Role || (IsPathVisible && !string.Equals(Path, loadedPath, StringComparison.Ordinal));
    }

    /// <summary>
    /// Picks the working path.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await _dialogs.PickFolderAsync(PathLabel);
        if (folder is not null)
        {
            Path = folder;
        }
    }

    /// <summary>
    /// Saves the role and path to the configuration file.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task ApplyRoleAsync() => RunSafeAsync(async () =>
    {
        var path = string.IsNullOrWhiteSpace(Path) ? null : Path.Trim();
        var result = await _api.SetRoleAsync(new SetRoleRequest(
            SelectedRole,
            SelectedRole == NodeRole.Origin ? path : null,
            SelectedRole == NodeRole.Workstation ? path : null));
        IsDirty = false;
        if (result.RestartRequired)
        {
            InfoMessage = Localizer.Get("Roles.RoleSavedRestarting");
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            InfoMessage = result.Message;
        }
    });

    /// <summary>
    /// Exports the public node configuration to a file.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialog = new ExportDialogViewModel(Localizer, _api);
        _ = dialog.ProbeCommand.ExecuteAsync(null);
        if (!await _dialogs.ShowExportDialogAsync(dialog))
        {
            return;
        }

        var file = await _dialogs.PickSaveFileAsync(Localizer.Get("Export.Title"), $"{_loaded?.NodeId.Value ?? "node"}.rcynq.json");
        if (file is null)
        {
            return;
        }

        await RunSafeAsync(async () =>
        {
            var exported = await _api.ExportConfigAsync(
                NullIfEmpty(dialog.Host),
                NullIfEmpty(dialog.User),
                (int)dialog.SshPort,
                dialog.ExportedSshPublicKey,
                dialog.ExportedHostKeyFingerprint);
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(exported, _json));
            InfoMessage = Localizer.Format("Roles.Exported", file);
        });
    }

    /// <summary>
    /// Imports a peer configuration file.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task ImportAsync()
    {
        var file = await _dialogs.PickOpenFileAsync(Localizer.Get("Roles.ImportTitle"));
        if (file is null)
        {
            return;
        }

        ExportedNodeConfiguration? configuration = null;
        ImportResult? result = null;
        if (!await RunSafeAsync(async () =>
        {
            configuration = JsonSerializer.Deserialize<ExportedNodeConfiguration>(await File.ReadAllTextAsync(file), _json)
                ?? throw new FormatException(Localizer.Get("Roles.ImportEmpty"));
            result = await _api.ImportConfigAsync(configuration);
        }))
        {
            return;
        }

        if (result!.Decision is not null)
        {
            // A node of this role is known already; nothing was imported until the user says what the file is.
            var dialog = new ImportDecisionDialogViewModel(Localizer, result);
            if (!await _dialogs.ShowImportDecisionDialogAsync(dialog) ||
                !await RunSafeAsync(async () => result = await _api.ImportConfigAsync(configuration!, dialog.Decision, dialog.LinkId)))
            {
                return;
            }
        }

        NotifyDataChanged();
        await RunQuietAsync(RefreshNodesAsync);
        await AllowAccessCoreAsync(Localizer.Format("Roles.Imported", result.PeerId.Value));
    }

    /// <summary>
    /// Shows the routes of a link as the service reported them.
    /// </summary>
    /// <param name="routes">The routes of one link.</param>
    public void ApplyRoutes(LinkRoutesDto routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        Nodes.FirstOrDefault(node => node.LinkId == routes.LinkId)?.ApplyRoutes(routes.Routes.Count == 0 ? null : routes);
    }

    /// <summary>
    /// Pins a route by hand: the node is reached this way whether the route answers or not.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task SelectRouteAsync(LinkRouteItemViewModel? route)
        => route is null
            ? Task.CompletedTask
            : RunSafeAsync(async () => ApplyRoutes(await _api.SelectLinkRouteAsync(route.LinkId, route.RouteId)));

    /// <summary>
    /// Hands the choice of the route back to the node.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task AutoRouteAsync(ChainNodeItemViewModel? node)
        => node is null
            ? Task.CompletedTask
            : RunSafeAsync(async () => ApplyRoutes(await _api.SelectLinkRouteAsync(node.LinkId, null)));

    /// <summary>
    /// Makes a route more preferred.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task RaiseRouteAsync(LinkRouteItemViewModel? route) => MoveRouteAsync(route, -1);

    /// <summary>
    /// Makes a route less preferred.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task LowerRouteAsync(LinkRouteItemViewModel? route) => MoveRouteAsync(route, 1);

    /// <summary>
    /// Removes a route after confirmation.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task RemoveRouteAsync(LinkRouteItemViewModel? route)
    {
        if (route is null || !await _dialogs.ConfirmAsync(Localizer.Get("Routes.Remove"), Localizer.Format("Routes.RemoveConfirm", route.Address)))
        {
            return;
        }

        await RunSafeAsync(async () => ApplyRoutes(await _api.RemoveLinkRouteAsync(route.LinkId, route.RouteId)));
    }

    /// <summary>
    /// Changes the preference of a route by a step; the most preferred value is zero.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="step">The change of the preference.</param>
    /// <returns>A task representing the operation.</returns>
    private Task MoveRouteAsync(LinkRouteItemViewModel? route, int step)
        => route is null || route.Priority + step < 0
            ? Task.CompletedTask
            : RunSafeAsync(async () => ApplyRoutes(await _api.SetLinkRoutePriorityAsync(route.LinkId, route.RouteId, route.Priority + step)));

    /// <summary>
    /// Describes a neighbour access result for the user.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The text.</returns>
    public string DescribeAccess(PeerAccessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = result.EntryCount == 0
            ? Localizer.Get("Roles.Access.None")
            : result.Applied ? Localizer.Format("Roles.Access.Allowed", result.EntryCount) : string.Empty;
        return (text + AccessNotes(result)).Trim();
    }

    /// <summary>
    /// Describes neighbours that still cannot be allowed.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The text, starting with a space, or an empty string.</returns>
    private string AccessNotes(PeerAccessResult result)
    {
        var notes = new StringBuilder();
        if (result.PeersWithoutKey.Count > 0)
        {
            notes.Append(Localizer.Format("Roles.Access.NoKey", string.Join(", ", result.PeersWithoutKey)));
        }

        if (result.PeersWaitingForCounterpart.Count > 0)
        {
            notes.Append(Localizer.Format("Roles.Access.NeedCounterpart", string.Join(", ", result.PeersWaitingForCounterpart)));
        }

        return notes.ToString();
    }

    /// <summary>
    /// Allows the imported neighbours in the SSH server settings of this computer.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task AllowAccessAsync() => AllowAccessCoreAsync(null);

    /// <summary>
    /// Updates the SSH server settings, asking for administrator rights when Windows requires them.
    /// </summary>
    /// <param name="prefix">Text shown before the result, or <see langword="null"/>.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task AllowAccessCoreAsync(string? prefix)
    {
        PeerAccessResult? result = null;
        if (!await RunSafeAsync(async () => result = await _api.AllowPeerAccessAsync()))
        {
            return;
        }

        var lead = prefix is null ? string.Empty : prefix + " ";
        if (!result!.RequiresElevation)
        {
            InfoMessage = lead + DescribeAccess(result);
            return;
        }

        if (_elevation is not { IsAvailable: true })
        {
            InfoMessage = lead + Localizer.Format("Roles.Access.RunManually", result.FilePath);
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            Localizer.Get("Roles.AllowAccess"),
            Localizer.Format("Roles.Access.Confirm", result.EntryCount, result.FilePath));
        if (!confirmed)
        {
            InfoMessage = lead + Localizer.Get("Roles.Access.Declined");
            return;
        }

        var run = await _elevation.AllowPeerAccessAsync();
        switch (run.Outcome)
        {
            case ElevatedRunOutcome.Completed:
                InfoMessage = (lead + run.Message + AccessNotes(result)).Trim();
                break;
            case ElevatedRunOutcome.Cancelled:
                InfoMessage = lead + run.Message;
                break;
            default:
                ErrorMessage = run.Message;
                break;
        }
    }

    /// <summary>
    /// Adds a node manually.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task AddNodeAsync()
    {
        var dialog = new AddNodeDialogViewModel(Localizer);
        if (!await _dialogs.ShowAddNodeDialogAsync(dialog))
        {
            return;
        }

        if (await RunSafeAsync(() => _api.SaveLinkAsync(dialog.ToOptions())))
        {
            NotifyDataChanged();
            await RefreshNodesAsync();
        }
    }

    /// <summary>
    /// Removes a node after confirmation.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task RemoveNodeAsync(ChainNodeItemViewModel? node)
    {
        if (node is null || !await _dialogs.ConfirmAsync(Localizer.Get("Roles.RemoveNode"), Localizer.Format("Roles.RemoveNodeConfirm", node.Name)))
        {
            return;
        }

        if (await RunSafeAsync(() => _api.DeleteLinkAsync(node.LinkId)))
        {
            Nodes.Remove(node);
            NotifyDataChanged();
        }
    }

    /// <summary>
    /// Reloads only the node list.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    private async Task RefreshNodesAsync()
    {
        var links = await _api.GetLinksAsync();
        var routes = await _api.GetLinkRoutesAsync() ?? Array.Empty<LinkRoutesDto>();
        Nodes.Clear();
        foreach (var link in links)
        {
            var node = new ChainNodeItemViewModel(Localizer, link);
            node.ApplyRoutes(routes.FirstOrDefault(candidate => candidate.LinkId == link.Id));
            Nodes.Add(node);
        }
    }

    /// <summary>
    /// Converts blank text to <see langword="null"/>.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>The trimmed text or <see langword="null"/>.</returns>
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
