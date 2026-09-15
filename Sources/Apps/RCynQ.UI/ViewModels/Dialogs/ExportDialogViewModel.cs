using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.UI.Services.Api;
using RCynQ.UI.ViewModels.Formatting;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The export dialog: detects the address, SSH user, SSH key and host key with operating system tools and lets the
/// user review them before the export file is written.
/// </summary>
public sealed partial class ExportDialogViewModel : DialogViewModelBase
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The advertised host.</summary>
    [ObservableProperty]
    private string _host = string.Empty;

    /// <summary>The advertised SSH user.</summary>
    [ObservableProperty]
    private string _user = string.Empty;

    /// <summary>The advertised SSH port.</summary>
    [ObservableProperty]
    private decimal _sshPort = 22;

    /// <summary>The validation error.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>Whether the environment is being probed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private bool _isProbing;

    /// <summary>The probe failure.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProbeError))]
    private string? _probeError;

    /// <summary>The selected detected address.</summary>
    [ObservableProperty]
    private AddressOptionViewModel? _selectedAddress;

    /// <summary>The SSH public key line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSshKey), nameof(CanGenerateKey))]
    private string? _sshPublicKey;

    /// <summary>The SSH key description.</summary>
    [ObservableProperty]
    private string _sshKeyDescription = "Определяется…";

    /// <summary>Whether the SSH public key is exported.</summary>
    [ObservableProperty]
    private bool _includeSshKey;

    /// <summary>The sshd host key fingerprint.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHostKey))]
    private string? _hostKeyFingerprint;

    /// <summary>Whether the host key fingerprint is exported.</summary>
    [ObservableProperty]
    private bool _includeHostKey;

    /// <summary>The sshd check label.</summary>
    [ObservableProperty]
    private string _sshdStatus = "SSH-сервер: проверяется…";

    /// <summary>Whether sshd accepts connections.</summary>
    [ObservableProperty]
    private bool _sshdOk;

    /// <summary>The service port check label.</summary>
    [ObservableProperty]
    private string _servicePortStatus = "Порт сервиса: проверяется…";

    /// <summary>Whether the service port accepts connections.</summary>
    [ObservableProperty]
    private bool _servicePortOk;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportDialogViewModel"/> class.
    /// </summary>
    /// <param name="api">The API client used for probing.</param>
    public ExportDialogViewModel(IServiceApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        Warnings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasWarnings));
        Addresses.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAddresses));
    }

    /// <summary>Gets the detected addresses, best first.</summary>
    public ObservableCollection<AddressOptionViewModel> Addresses { get; } = new();

    /// <summary>Gets the warnings.</summary>
    public ObservableCollection<string> Warnings { get; } = new();

    /// <summary>Gets a value indicating whether addresses were detected.</summary>
    public bool HasAddresses => Addresses.Count > 0;

    /// <summary>Gets a value indicating whether warnings exist.</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Gets a value indicating whether the probe failed.</summary>
    public bool HasProbeError => !string.IsNullOrEmpty(ProbeError);

    /// <summary>Gets a value indicating whether an SSH key was found.</summary>
    public bool HasSshKey => !string.IsNullOrEmpty(SshPublicKey);

    /// <summary>Gets a value indicating whether an SSH key can be generated.</summary>
    public bool CanGenerateKey => !HasSshKey;

    /// <summary>Gets a value indicating whether a host key fingerprint was found.</summary>
    public bool HasHostKey => !string.IsNullOrEmpty(HostKeyFingerprint);

    /// <summary>Gets a value indicating whether the export can be confirmed.</summary>
    public bool CanConfirm => !IsProbing;

    /// <summary>Gets the SSH public key to export, or <see langword="null"/>.</summary>
    public string? ExportedSshPublicKey => IncludeSshKey && HasSshKey ? SshPublicKey : null;

    /// <summary>Gets the host key fingerprint to export, or <see langword="null"/>.</summary>
    public string? ExportedHostKeyFingerprint => IncludeHostKey && HasHostKey ? HostKeyFingerprint : null;

    /// <summary>
    /// Applies a probe report to the form. Values the user already typed for the user name are kept.
    /// </summary>
    /// <param name="report">The report.</param>
    public void Apply(NodeEnvironmentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Addresses.Clear();
        foreach (var address in report.Addresses)
        {
            Addresses.Add(new AddressOptionViewModel(address));
        }

        SelectedAddress = Addresses.FirstOrDefault(option => option.Address == report.SuggestedHost) ?? Addresses.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(User))
        {
            User = report.SshUser;
        }

        SshPort = report.SshPort;
        SshdOk = report.SshdListening;
        SshdStatus = report.SshdListening
            ? $"SSH-сервер принимает подключения на порту {report.SshPort}"
            : $"SSH-сервер не отвечает на порту {report.SshPort}";
        ServicePortOk = report.ServicePortListening;
        ServicePortStatus = report.ServicePortListening ? "Порт сервиса RCynQ отвечает" : "Порт сервиса RCynQ не отвечает";

        SshPublicKey = report.SshPublicKey;
        SshKeyDescription = report.SshPublicKey is null ? "SSH-ключ не найден" : $"{report.SshKeyFingerprint} · {report.SshPublicKeyPath}";
        IncludeSshKey = HasSshKey;

        HostKeyFingerprint = report.HostKeyFingerprints.FirstOrDefault();
        IncludeHostKey = HasHostKey;

        Warnings.Clear();
        foreach (var warning in report.Warnings)
        {
            Warnings.Add(DisplayText.Warning(warning));
        }
    }

    /// <summary>
    /// Copies the selected address into the host field.
    /// </summary>
    /// <param name="value">The new selection.</param>
    partial void OnSelectedAddressChanged(AddressOptionViewModel? value)
    {
        if (value is not null)
        {
            Host = value.Address;
        }
    }

    /// <summary>
    /// Probes the environment through the service.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task ProbeAsync() => RunProbeAsync(() => _api.ProbeExportAsync());

    /// <summary>
    /// Generates the node SSH key and probes again.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task GenerateKeyAsync() => RunProbeAsync(() => _api.GenerateSshKeyAsync());

    /// <summary>
    /// Validates the input and confirms the dialog.
    /// </summary>
    [RelayCommand]
    private void Confirm()
    {
        var host = Host.Trim();
        if (host.Length == 0)
        {
            Error = "Укажите адрес, по которому соседи подключаются к этому узлу.";
            return;
        }

        if (!SshEndpoint.IsValidHost(host))
        {
            Error = "Недопустимый адрес.";
            return;
        }

        if (User.Length > 0 && !SshEndpoint.IsValidUser(User.Trim()))
        {
            Error = "Недопустимое имя пользователя SSH.";
            return;
        }

        if (SshPort is < 1 or > 65535)
        {
            Error = "Порт должен быть в диапазоне 1..65535.";
            return;
        }

        Error = null;
        Close(true);
    }

    /// <summary>
    /// Cancels the dialog.
    /// </summary>
    [RelayCommand]
    private void Cancel() => Close(false);

    /// <summary>
    /// Runs a probe call and applies its report.
    /// </summary>
    /// <param name="probe">The call.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task RunProbeAsync(Func<Task<NodeEnvironmentReport>> probe)
    {
        IsProbing = true;
        ProbeError = null;
        try
        {
            Apply(await probe());
        }
        catch (ServiceApiException ex)
        {
            ProbeError = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            ProbeError = $"Сервис RCynQ недоступен: {ex.Message}";
        }
        finally
        {
            IsProbing = false;
        }
    }
}
