using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Setup;

/// <summary>
/// First-run setup: creates or repairs <c>syncbridge.json</c> so the UI can start the service in the chosen role.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    /// <summary>The dialog service.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>The node identifier.</summary>
    [ObservableProperty]
    private string _nodeId = string.Empty;

    /// <summary>The display name.</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>The role.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PathLabel), nameof(IsPathVisible), nameof(IsTokenVisible), nameof(RoleHint), nameof(IsEndpointModeVisible), nameof(IsEndpointPortVisible))]
    private NodeRole _selectedRole = NodeRole.Source;

    /// <summary>The working copy or repository path.</summary>
    [ObservableProperty]
    private string _path = string.Empty;

    /// <summary>The service port.</summary>
    [ObservableProperty]
    private decimal _listenPort = 18443;

    /// <summary>The shared link token.</summary>
    [ObservableProperty]
    private string _linkToken = string.Empty;

    /// <summary>How neighbours reach this node.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEndpointPortVisible), nameof(EndpointHint))]
    private EndpointModeOption _selectedEndpointMode = EndpointModeOption.All[0];

    /// <summary>The TCP port of the direct endpoint.</summary>
    [ObservableProperty]
    private decimal _endpointPort = 18444;

    /// <summary>The problem to show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <summary>Whether the configuration is being written.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupViewModel"/> class.
    /// </summary>
    /// <param name="configPath">The configuration file to create or update.</param>
    /// <param name="dialogs">The dialog service.</param>
    public SetupViewModel(string configPath, IDialogService dialogs)
    {
        ConfigPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    /// <summary>
    /// Raised after the configuration was written.
    /// </summary>
    public event EventHandler? Completed;

    /// <summary>Gets the configuration file.</summary>
    public string ConfigPath { get; }

    /// <summary>Gets the selectable roles.</summary>
    public IReadOnlyList<NodeRole> Roles { get; } = new[] { NodeRole.Source, NodeRole.Mediator, NodeRole.Dest };

    /// <summary>Gets the label of the path field.</summary>
    public string PathLabel => SelectedRole == NodeRole.Dest ? "Путь к репозиторию" : "Рабочая копия";

    /// <summary>Gets a value indicating whether the path applies to the role.</summary>
    public bool IsPathVisible => SelectedRole != NodeRole.Mediator;

    /// <summary>Gets a value indicating whether the link token applies to the role.</summary>
    public bool IsTokenVisible => SelectedRole != NodeRole.Mediator;

    /// <summary>Gets the connection modes offered.</summary>
    public IReadOnlyList<EndpointModeOption> EndpointModes { get; } = EndpointModeOption.All;

    /// <summary>Gets a value indicating whether the connection mode can be chosen: a Mediator always uses its SSH server.</summary>
    public bool IsEndpointModeVisible => SelectedRole != NodeRole.Mediator;

    /// <summary>Gets a value indicating whether the port of the direct endpoint is shown.</summary>
    public bool IsEndpointPortVisible => IsEndpointModeVisible && SelectedEndpointMode.Value == EndpointMode.Direct;

    /// <summary>Gets the connection mode that is saved: a Mediator always uses its SSH server.</summary>
    private EndpointMode EffectiveEndpointMode => IsEndpointModeVisible ? SelectedEndpointMode.Value : EndpointMode.SshTunnel;

    /// <summary>Gets the explanation of the selected connection mode.</summary>
    public string EndpointHint => SelectedEndpointMode.Hint;

    /// <summary>Gets a value indicating whether a problem is shown.</summary>
    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>Gets the description of the selected role.</summary>
    public string RoleHint => SelectedRole switch
    {
        NodeRole.Source => "Рабочая копия разработчика: отправляет изменения на Dest, принимает их обратно и получает ветки Git.",
        NodeRole.Dest => "Закрытый репозиторий: принимает изменения от Source, отправляет свои и отдаёт ветки через git-upload-pack.",
        _ => "Ретранслятор: поднимает туннели и пропускает SSH-соединения соседей. Всё запускается вместе с приложением и останавливается при его закрытии."
    };

    /// <summary>
    /// Creates a valid node identifier from a machine name.
    /// </summary>
    /// <param name="machineName">The machine name.</param>
    /// <returns>The identifier.</returns>
    public static string DefaultNodeId(string? machineName)
    {
        var builder = new StringBuilder();
        foreach (var c in (machineName ?? string.Empty).Trim().ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-');
        }

        var id = builder.ToString().Trim('-', '.', '_');
        if (id.Length > 64)
        {
            id = id[..64];
        }

        return id.Length == 0 ? "node" : id;
    }

    /// <summary>
    /// Creates a random link token.
    /// </summary>
    /// <returns>The token.</returns>
    public static string CreateToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Fills the form from an existing configuration or with defaults.
    /// </summary>
    /// <param name="existing">The existing options, or <see langword="null"/>.</param>
    /// <param name="error">The problem that opened the setup, or <see langword="null"/>.</param>
    public void Load(SyncBridgeOptions? existing, string? error)
    {
        if (existing is null)
        {
            NodeId = DefaultNodeId(Environment.MachineName);
            Name = Environment.MachineName;
            SelectedRole = NodeRole.Source;
            Path = string.Empty;
            ListenPort = 18443;
            LinkToken = CreateToken();
            SelectedEndpointMode = EndpointModeOption.All[0];
            EndpointPort = 18444;
        }
        else
        {
            NodeId = string.IsNullOrWhiteSpace(existing.NodeId) ? DefaultNodeId(Environment.MachineName) : existing.NodeId;
            Name = string.IsNullOrWhiteSpace(existing.Name) ? Environment.MachineName : existing.Name;
            SelectedRole = existing.Role;
            Path = (existing.Role == NodeRole.Dest ? existing.RepoPath : existing.WorkdirPath) ?? string.Empty;
            ListenPort = existing.ListenPort is >= 1 and <= 65535 ? existing.ListenPort : 18443;
            LinkToken = existing.LinkToken ?? string.Empty;
            SelectedEndpointMode = EndpointModeOption.For(existing.Endpoint.Mode);
            EndpointPort = existing.Endpoint.Port is >= 1 and <= 65535 ? existing.Endpoint.Port : 18444;
        }

        Error = error;
    }

    /// <summary>
    /// Picks the path.
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
    /// Generates a new link token.
    /// </summary>
    [RelayCommand]
    private void GenerateToken() => LinkToken = CreateToken();

    /// <summary>
    /// Validates and writes the configuration.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Core.Ids.NodeId.TryParse(NodeId.Trim(), out _))
        {
            Error = "Идентификатор узла может содержать только латинские буквы, цифры, '-', '_' и '.'.";
            return;
        }

        if (IsPathVisible && string.IsNullOrWhiteSpace(Path))
        {
            Error = $"Укажите: {PathLabel.ToLowerInvariant()}.";
            return;
        }

        IsBusy = true;
        try
        {
            var errors = await LocalConfigFile.SaveAsync(
                ConfigPath,
                new NodeSetupValues(NodeId, Name, SelectedRole, Path, (int)ListenPort, LinkToken, EffectiveEndpointMode, (int)EndpointPort));
            if (errors.Count > 0)
            {
                Error = string.Join(Environment.NewLine, errors);
                return;
            }

            Error = null;
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"Не удалось записать {ConfigPath}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
