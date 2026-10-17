using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Localization;
using RCynQ.Ssh.Server;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Wizard;

/// <summary>
/// The connect wizard: leads through the setup of a link with invitations. It asks whether the way leads through a
/// Relay, makes sure this computer accepts neighbours, then lets the node join with an invitation or invite a node,
/// while a picture of the chain shows what happens.
/// </summary>
public sealed partial class ConnectWizardViewModel : DialogViewModelBase
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Asks for administrator rights to write <c>authorized_keys</c>, or <see langword="null"/>.</summary>
    private readonly Func<Task>? _allowAccess;

    /// <summary>The page shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScenarioStep), nameof(IsReadinessStep), nameof(IsActionStep), nameof(IsInviteStep), nameof(IsJoinStep), nameof(IsDoneStep), nameof(CanGoBack))]
    private WizardStep _step;

    /// <summary>Whether the way leads through a Relay.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChainLabels), nameof(LinkCount), nameof(ActionHint), nameof(IsDirect))]
    private bool _throughRelay = true;

    /// <summary>Whether this computer accepts neighbours.</summary>
    [ObservableProperty]
    private bool _isReady;

    /// <summary>What the check of this computer found.</summary>
    [ObservableProperty]
    private string _readinessText = string.Empty;

    /// <summary>
    /// The note on an administrator account of Windows, whose authorized_keys only an administrator may change, or
    /// <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdminNote))]
    private string? _adminNote;

    /// <summary>Whether this computer is being checked.</summary>
    [ObservableProperty]
    private bool _isChecking;

    /// <summary>Whether bytes run along every link that is not finished.</summary>
    [ObservableProperty]
    private bool _chainActive;

    /// <summary>The link of the picture a joining works on now, or -1.</summary>
    [ObservableProperty]
    private int _activeLink = -1;

    /// <summary>The role of the inviting node of the joining that runs, once the service read the invitation.</summary>
    private NodeRole? _inviter;

    /// <summary>How many links of the picture are finished.</summary>
    [ObservableProperty]
    private int _completedLinks;

    /// <summary>The link of the picture that failed, or -1.</summary>
    [ObservableProperty]
    private int _failedLink = -1;

    /// <summary>The message of the last page.</summary>
    [ObservableProperty]
    private string _doneMessage = string.Empty;

    /// <summary>Whether the node joined through a Relay and should now invite its counterpart.</summary>
    [ObservableProperty]
    private bool _canInviteNext;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectWizardViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    /// <param name="role">The role of this node.</param>
    /// <param name="nodeId">The identifier of this node.</param>
    /// <param name="serverSetup">Installs the SSH server or opens the TLS port, or <see langword="null"/>.</param>
    /// <param name="allowAccess">Asks for administrator rights to write <c>authorized_keys</c>, or <see langword="null"/>.</param>
    /// <param name="delay">Waits between two looks at the list of invitations; <see langword="null"/> for real time.</param>
    /// <param name="time">The clock that tells when an invitation expired; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    public ConnectWizardViewModel(
        ILocalizer localizer,
        IServiceApiClient api,
        IDialogService dialogs,
        NodeRole role,
        string nodeId,
        IServerSetup? serverSetup = null,
        Func<Task>? allowAccess = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeProvider? time = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _allowAccess = allowAccess;
        Role = role;

        Readiness = new ServerSetupPanelViewModel(localizer, serverSetup, CheckAsync);
        Invite = new InviteStepViewModel(localizer, api, dialogs, role, nodeId, delay, time);
        Join = new JoinStepViewModel(localizer, api, dialogs);
        Invite.Created += async (_, _) => await OnInvitationCreatedAsync();
        Invite.NodeJoined += (_, node) => OnNodeJoined(node);
        Invite.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(InviteStepViewModel.SelectedRole))
            {
                OnPropertyChanged(nameof(ChainLabels));
            }

            // An invitation that expired or was revoked is put away: nothing waits any more, and the page may be left.
            if (args.PropertyName == nameof(InviteStepViewModel.HasInvitation) && !Invite.HasInvitation)
            {
                ChainActive = false;
                OnPropertyChanged(nameof(CanGoBack));
            }
        };
        Join.Started += (_, _) => OnJoinStarted();
        Join.Finished += (_, result) => OnJoinFinished(result);

        _step = role == NodeRole.Relay ? WizardStep.Readiness : WizardStep.Scenario;
    }

    /// <summary>Gets the role of this node.</summary>
    public NodeRole Role { get; }

    /// <summary>Gets a value indicating whether this node is an endpoint, which may join; a Relay only invites.</summary>
    public bool IsEndpoint => Role != NodeRole.Relay;

    /// <summary>Gets a value indicating whether this node is a Workstation, which pulls the project when the link is set up.</summary>
    public bool IsWorkstation => Role == NodeRole.Workstation;

    /// <summary>Gets the explanation of the next invitation after this node joined a Relay.</summary>
    public string NextHint => _localizer.Format("Wizard.NextHint", Counterpart);

    /// <summary>Gets the role at the other end of the chain.</summary>
    public NodeRole Counterpart => Role == NodeRole.Workstation ? NodeRole.Origin : NodeRole.Workstation;

    /// <summary>Gets or sets a value indicating whether the link is direct; the counterpart of <see cref="ThroughRelay"/> for the radio buttons.</summary>
    public bool IsDirect
    {
        get => !ThroughRelay;
        set => ThroughRelay = !value;
    }

    /// <summary>Gets the buttons that make this computer accept neighbours.</summary>
    public ServerSetupPanelViewModel Readiness { get; }

    /// <summary>Gets the invite page.</summary>
    public InviteStepViewModel Invite { get; }

    /// <summary>Gets the join page.</summary>
    public JoinStepViewModel Join { get; }

    /// <summary>Gets the nodes of the picture, this node on the left.</summary>
    public IReadOnlyList<string> ChainLabels => Role == NodeRole.Relay
        ? new[] { NodeRole.Relay.ToString(), Invite.SelectedRole.ToString() }
        : ThroughRelay
            ? new[] { Role.ToString(), NodeRole.Relay.ToString(), Counterpart.ToString() }
            : new[] { Role.ToString(), Counterpart.ToString() };

    /// <summary>Gets the number of links of the picture.</summary>
    public int LinkCount => ChainLabels.Count - 1;

    /// <summary>Gets the explanation of what this node does next, for its role and the way.</summary>
    public string ActionHint => Role == NodeRole.Relay
        ? _localizer.Get("Wizard.Hint.Relay")
        : _localizer.Format(ThroughRelay ? "Wizard.Hint.EndpointRelay" : "Wizard.Hint.EndpointDirect", Counterpart);

    /// <summary>Gets a value indicating whether the note on an administrator account is shown.</summary>
    public bool HasAdminNote => !string.IsNullOrEmpty(AdminNote);

    /// <summary>Gets a value indicating whether the scenario page is shown.</summary>
    public bool IsScenarioStep => Step == WizardStep.Scenario;

    /// <summary>Gets a value indicating whether the readiness page is shown.</summary>
    public bool IsReadinessStep => Step == WizardStep.Readiness;

    /// <summary>Gets a value indicating whether the action page is shown.</summary>
    public bool IsActionStep => Step == WizardStep.Action;

    /// <summary>Gets a value indicating whether the invite page is shown.</summary>
    public bool IsInviteStep => Step == WizardStep.Invite;

    /// <summary>Gets a value indicating whether the join page is shown.</summary>
    public bool IsJoinStep => Step == WizardStep.Join;

    /// <summary>Gets a value indicating whether the last page is shown.</summary>
    public bool IsDoneStep => Step == WizardStep.Done;

    /// <summary>Gets a value indicating whether the previous page can be shown again.</summary>
    public bool CanGoBack => Step switch
    {
        WizardStep.Scenario or WizardStep.Done => false,
        WizardStep.Readiness => IsEndpoint,
        WizardStep.Invite => !Invite.HasInvitation,
        WizardStep.Join => !Join.IsJoining && !Join.Joined,
        _ => true
    };

    /// <summary>
    /// Shows a stage of the joining that runs: the stage in the list, and on the picture the link it works on, finished
    /// or failed. Reports that come while no joining runs are ignored.
    /// </summary>
    /// <param name="progress">The stage.</param>
    public void ApplyJoinProgress(JoinProgressEvent progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (Step != WizardStep.Join || !Join.IsJoining)
        {
            return;
        }

        Join.ApplyProgress(progress);
        _inviter = progress.Inviter ?? _inviter;
        var link = LinkOf(progress.Step);
        switch (progress.State)
        {
            case JoinStepState.Running when link is { } working:
                ChainActive = false;
                ActiveLink = working;
                break;
            case JoinStepState.Running when progress.Step == JoinStep.Access:
                ChainActive = false;
                ActiveLink = -1;
                break;
            case JoinStepState.Succeeded when link is { } done:
                CompletedLinks = Math.Max(CompletedLinks, done + 1);
                ActiveLink = -1;
                break;
            case JoinStepState.Failed:
                FailedLink = link ?? -1;
                ActiveLink = -1;
                ChainActive = false;
                break;
        }
    }

    /// <summary>
    /// Starts the wizard: a Relay opens on the readiness page, which checks this computer at once; an endpoint first
    /// chooses the way.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public Task StartAsync() => Step == WizardStep.Readiness ? CheckAsync() : Task.CompletedTask;

    /// <summary>
    /// Leaves the scenario page and checks this computer.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task NextAsync()
    {
        Step = WizardStep.Readiness;
        return CheckAsync();
    }

    /// <summary>
    /// Checks whether this computer accepts neighbours: its SSH server or its TLS port answers.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task CheckAsync()
    {
        IsChecking = true;
        Readiness.IsChecking = true;
        try
        {
            var report = await _api.ProbeExportAsync();
            var direct = report.EndpointMode == EndpointMode.Direct;
            IsReady = direct ? report.Addresses.Any(address => address.SshReachable) : report.SshdListening;
            ReadinessText = direct
                ? _localizer.Format(IsReady ? "Export.Direct.Ok" : "Export.Direct.Closed", report.EndpointPort)
                : _localizer.Format(IsReady ? "Export.Ssh.Ok" : "Export.Ssh.Closed", report.SshPort);
            Readiness.Apply(direct, IsReady, report.EndpointPort);

            // A Relay nobody sits at cannot confirm the administrator prompt each time a node joins.
            AdminNote = report.Warnings.Contains(EnvironmentWarning.AccessNeedsAdministrator)
                ? _localizer.Get(Role == NodeRole.Relay ? "Wizard.Admin.Relay" : "Wizard.Admin.Endpoint")
                : null;
        }
        catch (ServiceApiException ex)
        {
            IsReady = false;
            ReadinessText = _localizer.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            IsReady = false;
            ReadinessText = _localizer.Format("Common.ServiceUnavailable", _localizer.Describe(ex));
        }
        finally
        {
            IsChecking = false;
            Readiness.IsChecking = false;
        }
    }

    /// <summary>
    /// Leaves the readiness page; a Relay goes straight to inviting.
    /// </summary>
    [RelayCommand]
    private void Continue() => Step = IsEndpoint ? WizardStep.Action : WizardStep.Invite;

    /// <summary>
    /// Shows the invite page.
    /// </summary>
    [RelayCommand]
    private void ChooseInvite() => Step = WizardStep.Invite;

    /// <summary>
    /// Shows the join page.
    /// </summary>
    [RelayCommand]
    private void ChooseJoin() => Step = WizardStep.Join;

    /// <summary>
    /// Shows the invite page again after joining through a Relay: now this node invites its counterpart.
    /// </summary>
    [RelayCommand]
    private void InviteNext()
    {
        CanInviteNext = false;
        CompletedLinks = 1;
        FailedLink = -1;
        Step = WizardStep.Invite;
    }

    /// <summary>
    /// Shows the previous page.
    /// </summary>
    [RelayCommand]
    private void Back()
    {
        Step = Step switch
        {
            WizardStep.Readiness => WizardStep.Scenario,
            WizardStep.Action => WizardStep.Readiness,
            WizardStep.Invite or WizardStep.Join => IsEndpoint ? WizardStep.Action : WizardStep.Readiness,
            _ => Step
        };
    }

    /// <summary>
    /// Closes the wizard and stops waiting for an invited node.
    /// </summary>
    [RelayCommand]
    private void Finish()
    {
        Invite.Stop();
        Close(Step == WizardStep.Done);
    }

    /// <summary>
    /// Shows the waiting for the invited node, and asks for administrator rights when the one-time key needs them.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    private async Task OnInvitationCreatedAsync()
    {
        ChainActive = true;
        OnPropertyChanged(nameof(CanGoBack));

        if (Invite.AccessPending && _allowAccess is not null)
        {
            await _allowAccess();
        }
    }

    /// <summary>
    /// Shows that the invited node joined.
    /// </summary>
    /// <param name="node">The identifier of the node.</param>
    private void OnNodeJoined(string node)
    {
        ChainActive = false;
        CompletedLinks = LinkCount;
        DoneMessage = _localizer.Format("Wizard.Joined", node);
        Step = WizardStep.Done;
    }

    /// <summary>
    /// Shows that joining runs.
    /// </summary>
    private void OnJoinStarted()
    {
        _inviter = null;
        ActiveLink = -1;
        CompletedLinks = 0;
        FailedLink = -1;
        ChainActive = true;
        OnPropertyChanged(nameof(CanGoBack));
    }

    /// <summary>
    /// Shows how joining ended on the picture and, on success, the last page.
    /// </summary>
    /// <param name="result">How joining ended, or <see langword="null"/> when the service did not answer.</param>
    private void OnJoinFinished(JoinResult? result)
    {
        ChainActive = false;
        ActiveLink = -1;
        OnPropertyChanged(nameof(CanGoBack));
        if (result is { Joined: true })
        {
            // Joining a Relay links this node to the Relay only; the counterpart comes with the next invitation.
            var joinedRelay = _inviter is { } inviter
                ? inviter == NodeRole.Relay
                : result.Steps.Count > 0 && ThroughRelay && !result.Steps.Any(step => step.Step == JoinStep.Relay);
            CompletedLinks = joinedRelay ? 1 : LinkCount;
            CanInviteNext = joinedRelay;
            DoneMessage = result.Message;
            Step = WizardStep.Done;
            return;
        }

        var failed = result?.Steps.LastOrDefault(step => !step.Succeeded)?.Step;
        FailedLink = failed is { } stage ? LinkOf(stage) ?? -1 : -1;
    }

    /// <summary>
    /// Finds the link of the picture a stage works on: the Relay is the first one; the inviting node is reached over the
    /// first link when it is the Relay itself, otherwise over the last one.
    /// </summary>
    /// <param name="step">The stage.</param>
    /// <returns>The link, or <see langword="null"/> for a stage that works on no link.</returns>
    private int? LinkOf(JoinStep step) => step switch
    {
        JoinStep.Relay => 0,
        JoinStep.Inviter => _inviter == NodeRole.Relay ? 0 : LinkCount - 1,
        _ => null
    };
}
