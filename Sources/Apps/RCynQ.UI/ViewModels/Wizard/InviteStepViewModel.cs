using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Wizard;

/// <summary>
/// The invite page of the connect wizard: chooses the role of the invited node and the lifetime, creates the invitation,
/// hands it to the user to copy or save, and waits until the invited node joins.
/// </summary>
public sealed partial class InviteStepViewModel : ObservableObject
{
    /// <summary>How often the list of invitations is asked while waiting.</summary>
    public static readonly TimeSpan WaitInterval = TimeSpan.FromSeconds(3);

    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service, for the clipboard and the file picker.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The identifier of this node, which names a saved invitation.</summary>
    private readonly string _nodeId;

    /// <summary>Waits between two looks at the list of invitations.</summary>
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>The clock that tells when the invitation expired.</summary>
    private readonly TimeProvider _time;

    /// <summary>Stops the waiting when the wizard closes.</summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>The role of the invited node.</summary>
    [ObservableProperty]
    private NodeRole _selectedRole;

    /// <summary>The lifetime of the invitation.</summary>
    [ObservableProperty]
    private LifetimeOption _selectedLifetime;

    /// <summary>The invitation text, or <see langword="null"/> before it is created.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvitation), nameof(CanCreate))]
    private string? _invitationText;

    /// <summary>The warning that goes with the invitation.</summary>
    [ObservableProperty]
    private string _secretWarning = string.Empty;

    /// <summary>Whether the invitation is being created.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private bool _isBusy;

    /// <summary>Whether the page waits for the invited node.</summary>
    [ObservableProperty]
    private bool _isWaiting;

    /// <summary>The identifier of the node that joined, or <see langword="null"/>.</summary>
    [ObservableProperty]
    private string? _joinedBy;

    /// <summary>The error of the last action, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <summary>The message of the last action, or <see langword="null"/>.</summary>
    [ObservableProperty]
    private string? _info;

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteStepViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    /// <param name="ownRole">The role of this node, which decides whom it may invite.</param>
    /// <param name="nodeId">The identifier of this node.</param>
    /// <param name="delay">Waits between two looks at the list of invitations; <see cref="Task.Delay(TimeSpan, CancellationToken)"/> when <see langword="null"/>.</param>
    /// <param name="time">The clock that tells when the invitation expired; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    public InviteStepViewModel(
        ILocalizer localizer,
        IServiceApiClient api,
        IDialogService dialogs,
        NodeRole ownRole,
        string nodeId,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeProvider? time = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _nodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
        _delay = delay ?? Task.Delay;
        _time = time ?? TimeProvider.System;

        Roles = ownRole switch
        {
            NodeRole.Relay => new[] { NodeRole.Origin, NodeRole.Workstation },
            NodeRole.Workstation => new[] { NodeRole.Origin },
            _ => new[] { NodeRole.Workstation }
        };
        Lifetimes = new[] { 1, 24, 168 }.Select(hours => new LifetimeOption(hours, _localizer.Get($"Invite.Lifetime.{hours}"))).ToArray();
        _selectedRole = Roles[0];
        _selectedLifetime = Lifetimes[1];
    }

    /// <summary>Occurs when the invited node joined; the argument is its identifier.</summary>
    public event EventHandler<string>? NodeJoined;

    /// <summary>Gets the roles this node may invite.</summary>
    public IReadOnlyList<NodeRole> Roles { get; }

    /// <summary>Gets a value indicating whether more than one role can be invited, so the choice is shown.</summary>
    public bool HasRoleChoice => Roles.Count > 1;

    /// <summary>Gets the lifetimes to choose from.</summary>
    public IReadOnlyList<LifetimeOption> Lifetimes { get; }

    /// <summary>Gets the notes of the created invitation.</summary>
    public ObservableCollection<string> Notes { get; } = new();

    /// <summary>Gets a value indicating whether the invitation exists.</summary>
    public bool HasInvitation => InvitationText is not null;

    /// <summary>Gets a value indicating whether an invitation can be created now.</summary>
    public bool CanCreate => !IsBusy && !HasInvitation;

    /// <summary>Gets a value indicating whether there is an error.</summary>
    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>
    /// Gets a value indicating whether the one-time key of the invitation still waits for administrator rights.
    /// </summary>
    public bool AccessPending { get; private set; }

    /// <summary>Gets the waiting for the invited node, or a completed task before an invitation exists.</summary>
    public Task Waiting { get; private set; } = Task.CompletedTask;

    /// <summary>Occurs when an invitation was created; the wizard asks for administrator rights when they are needed.</summary>
    public event EventHandler? Created;

    /// <summary>
    /// Stops waiting for the invited node.
    /// </summary>
    public void Stop() => _stop.Cancel();

    /// <summary>
    /// Creates the invitation and starts waiting for the invited node.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task CreateAsync()
    {
        IsBusy = true;
        Error = null;
        CreatedInvitation created;
        try
        {
            created = await _api.CreateInvitationAsync(new CreateInvitationRequest(SelectedRole, SelectedLifetime.Hours));
        }
        catch (ServiceApiException ex)
        {
            Error = _localizer.Describe(ex);
            return;
        }
        catch (HttpRequestException ex)
        {
            Error = _localizer.Format("Common.ServiceUnavailable", _localizer.Describe(ex));
            return;
        }
        finally
        {
            IsBusy = false;
        }

        AccessPending = created.AccessPending;
        Notes.Clear();
        foreach (var note in created.Notes)
        {
            Notes.Add(note);
        }

        SecretWarning = _localizer.Format("Invite.Secret", created.Summary.ExpiresAtUtc.ToLocalTime().ToString("g", _localizer.Culture));
        InvitationText = created.Text;
        Created?.Invoke(this, EventArgs.Empty);
        Waiting = WaitForJoinAsync(created.Summary.Id, _stop.Token);
    }

    /// <summary>
    /// Copies the invitation to the clipboard.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task CopyAsync()
    {
        if (InvitationText is not null && await _dialogs.CopyToClipboardAsync(InvitationText))
        {
            Info = _localizer.Get("Invite.Copied");
        }
    }

    /// <summary>
    /// Saves the invitation to a file.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (InvitationText is null)
        {
            return;
        }

        var file = await _dialogs.PickSaveFileAsync(_localizer.Get("Invite.SaveTitle"), $"{_nodeId}-{SelectedRole.ToString().ToLowerInvariant()}.rcynq-invite");
        if (file is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(file, InvitationText + Environment.NewLine);
            Info = _localizer.Format("Invite.Saved", file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = _localizer.Describe(ex);
        }
    }

    /// <summary>
    /// Looks at the list of invitations until the invitation is used, revoked, expired or the wizard closes. A service
    /// that does not answer for a moment does not stop the waiting. An invitation nobody can use any more is put away,
    /// so a new one can be created.
    /// </summary>
    /// <param name="id">The ticket identifier of the invitation.</param>
    /// <param name="cancellationToken">Stops the waiting.</param>
    /// <returns>A task representing the waiting.</returns>
    private async Task WaitForJoinAsync(Guid id, CancellationToken cancellationToken)
    {
        IsWaiting = true;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _delay(WaitInterval, cancellationToken);
                InvitationDto? invitation;
                try
                {
                    invitation = (await _api.GetInvitationsAsync(cancellationToken)).FirstOrDefault(item => item.Id == id);
                }
                catch (Exception ex) when (ex is ServiceApiException or HttpRequestException)
                {
                    continue;
                }

                if (invitation?.UsedBy is { } node)
                {
                    JoinedBy = node;
                    NodeJoined?.Invoke(this, node);
                    return;
                }

                if (invitation is null || invitation.IsRevoked || invitation.ExpiresAtUtc <= _time.GetUtcNow())
                {
                    InvitationText = null;
                    Notes.Clear();
                    Info = null;
                    Error = _localizer.Get("Invite.Closed");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The wizard closed.
        }
        finally
        {
            IsWaiting = false;
        }
    }
}
