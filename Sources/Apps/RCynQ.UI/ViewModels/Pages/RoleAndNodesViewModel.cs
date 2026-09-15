using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Items;

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

    /// <summary>The loaded configuration.</summary>
    private NodeConfigurationDto? _loaded;

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
    public RoleAndNodesViewModel(IServiceApiClient api, IDialogService dialogs, IPeerAccessElevation? elevation = null)
    {
        _api = api;
        _dialogs = dialogs;
        _elevation = elevation;
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

    /// <inheritdoc />
    public override Task RefreshAsync() => RunSafeAsync(async () =>
    {
        _loaded = await _api.GetConfigAsync();
        SelectedRole = _loaded.Role;
        Path = (_loaded.Role == NodeRole.Dest ? _loaded.RepoPath : _loaded.WorkdirPath) ?? string.Empty;
        IsDirty = false;

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
    partial void OnSelectedRoleChanged(NodeRole value) => UpdateDirty();

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
