using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Localization;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Wizard;

/// <summary>
/// The join page of the connect wizard: takes an invitation pasted or opened from a file, joins the node that wrote it
/// and shows how every stage ended.
/// </summary>
public sealed partial class JoinStepViewModel : ObservableObject
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service, for the file picker.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The invitation text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanJoin))]
    private string _invitation = string.Empty;

    /// <summary>Whether the node is joining.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanJoin))]
    private bool _isJoining;

    /// <summary>Whether the last joining succeeded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanJoin))]
    private bool _joined;

    /// <summary>The summary of the last joining, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _result;

    /// <summary>
    /// Initializes a new instance of the <see cref="JoinStepViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    public JoinStepViewModel(ILocalizer localizer, IServiceApiClient api, IDialogService dialogs)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    /// <summary>Occurs when joining starts.</summary>
    public event EventHandler? Started;

    /// <summary>Occurs when joining ended; the argument says how, or is <see langword="null"/> when the service did not answer.</summary>
    public event EventHandler<JoinResult?>? Finished;

    /// <summary>Gets the stages of the last joining.</summary>
    public ObservableCollection<JoinStepItemViewModel> Steps { get; } = new();

    /// <summary>Gets a value indicating whether the node can join now.</summary>
    public bool CanJoin => !IsJoining && !Joined && !string.IsNullOrWhiteSpace(Invitation);

    /// <summary>Gets a value indicating whether a joining ended.</summary>
    public bool HasResult => Result is not null;

    /// <summary>
    /// Shows a stage the service reported while joining runs: a started stage appears as running, an ended one gets its
    /// outcome. Reports that come while no joining runs are ignored.
    /// </summary>
    /// <param name="progress">The stage.</param>
    public void ApplyProgress(JoinProgressEvent progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!IsJoining)
        {
            return;
        }

        var item = Steps.FirstOrDefault(step => step.Step == progress.Step);
        if (item is null)
        {
            item = new JoinStepItemViewModel(_localizer, progress.Step);
            Steps.Add(item);
        }

        if (progress.State != JoinStepState.Running)
        {
            item.Finish(progress.State == JoinStepState.Succeeded, progress.Message);
        }
    }

    /// <summary>
    /// Reads the invitation from a file.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var file = await _dialogs.PickOpenFileAsync(_localizer.Get("Join.OpenTitle"));
        if (file is null)
        {
            return;
        }

        try
        {
            Invitation = (await File.ReadAllTextAsync(file)).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Result = _localizer.Describe(ex);
        }
    }

    /// <summary>
    /// Joins the node that wrote the invitation.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task JoinAsync()
    {
        IsJoining = true;
        Steps.Clear();
        Result = null;
        Started?.Invoke(this, EventArgs.Empty);
        JoinResult? result = null;
        try
        {
            result = await _api.JoinAsync(Invitation);

            // The answer carries every stage; the ones the live reports showed already give way to it.
            Steps.Clear();
            foreach (var step in result.Steps)
            {
                Steps.Add(new JoinStepItemViewModel(_localizer, step));
            }

            Joined = result.Joined;
            Result = result.Message;
        }
        catch (ServiceApiException ex)
        {
            Joined = false;
            Result = _localizer.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            Joined = false;
            Result = _localizer.Format("Common.ServiceUnavailable", _localizer.Describe(ex));
        }
        finally
        {
            IsJoining = false;
        }

        Finished?.Invoke(this, result);
    }
}
