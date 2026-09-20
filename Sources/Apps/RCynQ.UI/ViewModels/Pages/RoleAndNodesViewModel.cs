using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Items;
using RCynQ.UI.ViewModels.Setup;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The "Роль и узлы" tab: role, working path, chain nodes, export and import.
/// </summary>
public sealed partial class RoleAndNodesViewModel : PageViewModelBase
{
    /// <summary>JSON options of exported files.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

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

    /// <summary>The shared token of Source and Dest.</summary>
    [ObservableProperty]
    private string _linkToken = string.Empty;

    /// <summary>Whether the shared token is shown as text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenMaskChar), nameof(RevealTokenText))]
    private bool _isTokenRevealed;

    /// <summary>How neighbours reach this node.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEndpointPortVisible), nameof(EndpointHint))]
    private EndpointModeOption _selectedEndpointMode = EndpointModeOption.All[0];

    /// <summary>The TCP port of the direct endpoint.</summary>
    [ObservableProperty]
    private decimal _endpointPort = 18444;

    /// <summary>Whether the settings differ from the configuration file.</summary>
    [ObservableProperty]
    private bool _areSettingsDirty;

    /// <summary>The selected role.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PathLabel), nameof(IsPathVisible))]
    private NodeRole _selectedRole = NodeRole.Source;

    /// <summary>The working path.</summary>
    [ObservableProperty]
    private string _path = string.Empty;

    /// <summary>Whether the role or path differ from the running configuration.</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoleAndNodesViewModel"/> class.
    /// </summary>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    /// <param name="elevation">Runs the SSH access update with administrator rights, or <see langword="null"/> when unavailable.</param>
    public RoleAndNodesViewModel(
        IServiceApiClient api,
        IDialogService dialogs,
        IPeerAccessElevation? elevation = null,
        ILocalNodeSettings? settings = null)
    {
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
    public IReadOnlyList<NodeRole> Roles { get; } = new[] { NodeRole.Source, NodeRole.Mediator, NodeRole.Dest };

    /// <summary>Gets the chain nodes.</summary>
    public ObservableCollection<ChainNodeItemViewModel> Nodes { get; } = new();

    /// <summary>Gets a value indicating whether no nodes are configured.</summary>
    public bool HasNoNodes => Nodes.Count == 0;

    /// <summary>Gets the label of the path field.</summary>
    public string PathLabel => SelectedRole == NodeRole.Dest ? "Путь к репозиторию" : "Рабочая копия";

    /// <summary>Gets a value indicating whether the path field applies.</summary>
    public bool IsPathVisible => SelectedRole != NodeRole.Mediator;

    /// <summary>Gets a value indicating whether the node settings card is shown.</summary>
    public bool AreSettingsVisible => _settings is not null;

    /// <summary>Gets a value indicating whether this node uses a shared token: a Mediator never does.</summary>
    public bool HasLinkToken => SelectedRole != NodeRole.Mediator;

    /// <summary>Gets the connection modes offered.</summary>
    public IReadOnlyList<EndpointModeOption> EndpointModes { get; } = EndpointModeOption.All;

    /// <summary>Gets a value indicating whether the connection mode can be chosen: a Mediator always uses its SSH server.</summary>
    public bool IsEndpointModeVisible => SelectedRole != NodeRole.Mediator;

    /// <summary>Gets a value indicating whether the port of the direct endpoint is shown.</summary>
    public bool IsEndpointPortVisible => IsEndpointModeVisible && SelectedEndpointMode.Value == EndpointMode.Direct;

    /// <summary>Gets the explanation of the selected connection mode.</summary>
    public string EndpointHint => SelectedEndpointMode.Hint;

    /// <summary>Gets the character hiding the token, or <c>'\0'</c> when it is revealed.</summary>
    public char TokenMaskChar => IsTokenRevealed ? '\0' : '•';

    /// <summary>Gets the caption of the reveal button.</summary>
    public string RevealTokenText => IsTokenRevealed ? "Скрыть" : "Показать";

    /// <summary>Gets the path of the configuration file, shown under the settings.</summary>
    public string ConfigPath => _settings?.ConfigPath ?? string.Empty;

    /// <inheritdoc />
    public override Task RefreshAsync() => RunSafeAsync(async () =>
    {
        _loaded = await _api.GetConfigAsync();
        SelectedRole = _loaded.Role;
        Path = (_loaded.Role == NodeRole.Dest ? _loaded.RepoPath : _loaded.WorkdirPath) ?? string.Empty;
        IsDirty = false;
        LoadSettings();

        Nodes.Clear();
        foreach (var link in await _api.GetLinksAsync())
        {
            Nodes.Add(new ChainNodeItemViewModel(link));
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

    partial void OnLinkTokenChanged(string value) => AreSettingsDirty = true;

    partial void OnSelectedEndpointModeChanged(EndpointModeOption value) => AreSettingsDirty = true;

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
        SelectedEndpointMode = EndpointModeOption.For(options.Endpoint.Mode);
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
            ? "Общий ключ скопирован. Вставьте его на противоположном узле (SOURCE или DEST) — он должен совпадать."
            : "Не удалось скопировать ключ. Нажмите «Показать» и скопируйте его вручную.";

    /// <summary>
    /// Replaces the shared token with a new one after confirmation.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task NewTokenAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Создать новый общий ключ",
            "Старый ключ перестанет действовать. Пока новый ключ не прописан на противоположном узле, обмен изменениями работать не будет. Продолжить?");
        if (confirmed)
        {
            LinkToken = SetupViewModel.CreateToken();
            IsTokenRevealed = true;
            InfoMessage = "Новый ключ создан. Сохраните настройки и перенесите ключ на противоположный узел.";
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
            ErrorMessage = "Файл конфигурации недоступен.";
            return;
        }

        await RunSafeAsync(async () =>
        {
            var values = new NodeSetupValues(
                options.NodeId,
                options.Name,
                options.Role,
                (options.Role == NodeRole.Dest ? options.RepoPath : options.WorkdirPath) ?? string.Empty,
                options.ListenPort,
                LinkToken,
                options.Role == NodeRole.Mediator ? EndpointMode.SshTunnel : SelectedEndpointMode.Value,
                (int)EndpointPort,
                options.Endpoint.BindAddress);
            var errors = await _settings.SaveAsync(values);
            if (errors.Count > 0)
            {
                ErrorMessage = string.Join(Environment.NewLine, errors);
                return;
            }

            AreSettingsDirty = false;
            InfoMessage = "Настройки сохранены, сервис перезапускается…";
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

        var loadedPath = (_loaded.Role == NodeRole.Dest ? _loaded.RepoPath : _loaded.WorkdirPath) ?? string.Empty;
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
            SelectedRole == NodeRole.Dest ? path : null,
            SelectedRole == NodeRole.Source ? path : null));
        IsDirty = false;
        if (result.RestartRequired)
        {
            InfoMessage = "Роль сохранена, сервис перезапускается…";
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
        var dialog = new ExportDialogViewModel(_api);
        _ = dialog.ProbeCommand.ExecuteAsync(null);
        if (!await _dialogs.ShowExportDialogAsync(dialog))
        {
            return;
        }

        var file = await _dialogs.PickSaveFileAsync("Экспорт конфигурации", $"{_loaded?.NodeId.Value ?? "node"}.rcynq.json");
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
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(exported, Json));
            InfoMessage = $"Конфигурация сохранена в {file}. Закрытые ключи не экспортируются.";
        });
    }

    /// <summary>
    /// Imports a peer configuration file.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task ImportAsync()
    {
        var file = await _dialogs.PickOpenFileAsync("Импорт конфигурации узла");
        if (file is null)
        {
            return;
        }

        string? importedPeer = null;
        if (await RunSafeAsync(async () =>
        {
            var configuration = JsonSerializer.Deserialize<ExportedNodeConfiguration>(await File.ReadAllTextAsync(file), Json)
                ?? throw new FormatException("Файл конфигурации пуст.");
            importedPeer = (await _api.ImportConfigAsync(configuration)).PeerId.Value;
        }))
        {
            await RefreshNodesAsync();
            await AllowAccessCoreAsync($"Узел {importedPeer} импортирован.");
        }
    }

    /// <summary>
    /// Describes a neighbour access result for the user.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The text.</returns>
    public static string DescribeAccess(PeerAccessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = result.EntryCount == 0
            ? "Пока ни одному соседнему узлу не нужно подключаться к этому компьютеру."
            : result.Applied ? $"Подключения соседних узлов разрешены ({result.EntryCount})." : string.Empty;
        return (text + AccessNotes(result)).Trim();
    }

    /// <summary>
    /// Describes neighbours that still cannot be allowed.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The text, starting with a space, or an empty string.</returns>
    private static string AccessNotes(PeerAccessResult result)
    {
        var notes = new StringBuilder();
        if (result.PeersWithoutKey.Count > 0)
        {
            notes.Append($" В файле узла {string.Join(", ", result.PeersWithoutKey)} нет SSH-ключа: попросите экспортировать конфигурацию заново с включённым ключом и импортируйте её ещё раз.");
        }

        if (result.PeersWaitingForCounterpart.Count > 0)
        {
            notes.Append($" Для узла {string.Join(", ", result.PeersWaitingForCounterpart)} импортируйте также файл второго конечного узла (SOURCE или DEST).");
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
            InfoMessage = lead + $"Чтобы соседние узлы могли подключаться, выполните «rcynq ssh allow» в терминале от имени администратора ({result.FilePath}).";
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Разрешить подключения узлов",
            $"RCynQ добавит ключи соседних узлов ({result.EntryCount}) в настройки SSH-сервера этого компьютера:{Environment.NewLine}{result.FilePath}{Environment.NewLine}{Environment.NewLine}"
            + "Каждому ключу разрешено только передавать соединения RCynQ; войти в систему по нему нельзя. Windows попросит подтвердить действие от имени администратора.");
        if (!confirmed)
        {
            InfoMessage = lead + "Подключения соседних узлов не разрешены. Это можно сделать позже кнопкой «Разрешить подключения узлов».";
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
        var dialog = new AddNodeDialogViewModel();
        if (!await _dialogs.ShowAddNodeDialogAsync(dialog))
        {
            return;
        }

        if (await RunSafeAsync(() => _api.SaveLinkAsync(dialog.ToOptions())))
        {
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
        if (node is null || !await _dialogs.ConfirmAsync("Удалить узел", $"Удалить звено к узлу {node.Name}? Импортированные ключи узла сохранятся."))
        {
            return;
        }

        if (await RunSafeAsync(() => _api.DeleteLinkAsync(node.LinkId)))
        {
            Nodes.Remove(node);
        }
    }

    /// <summary>
    /// Reloads only the node list.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    private async Task RefreshNodesAsync()
    {
        var links = await _api.GetLinksAsync();
        Nodes.Clear();
        foreach (var link in links)
        {
            Nodes.Add(new ChainNodeItemViewModel(link));
        }
    }

    /// <summary>
    /// Converts blank text to <see langword="null"/>.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>The trimmed text or <see langword="null"/>.</returns>
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
