using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Localization;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;

namespace RCynQ.UI.ViewModels.Repositories;

/// <summary>
/// The repositories of the node: which one it works with, switching to another one, and what to do when the node at the
/// other end switched.
/// </summary>
/// <remarks>
/// <para>
/// A Workstation that switches to a repository it does not keep yet asks where to put it, suggesting a folder next to
/// the current working copy, and then pulls the current version into it.
/// </para>
/// <para>
/// When the other node switched, this node does not follow by itself: a notice offers to switch too or to stay.
/// </para>
/// </remarks>
public sealed partial class RepositoriesViewModel : ObservableObject
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The role of the node.</summary>
    private NodeRole _role = NodeRole.Relay;

    /// <summary>The repository of the other node the user chose to stay away from; the notice is hidden for it.</summary>
    private string? _dismissedPeer;

    /// <summary>The name of the repository the node works with, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMismatch))]
    [NotifyPropertyChangedFor(nameof(MismatchText))]
    [NotifyPropertyChangedFor(nameof(ActiveText))]
    private string? _activeName;

    /// <summary>The name of the repository the node at the other end works with, or <see langword="null"/> when not known.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMismatch))]
    [NotifyPropertyChangedFor(nameof(MismatchText))]
    private string? _peerName;

    /// <summary>Whether an operation is running.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The last error, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>Why the Origin could not be asked for its repositories, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCatalogError))]
    private string? _catalogError;

    /// <summary>The repository selected in the switcher.</summary>
    [ObservableProperty]
    private RepositoryItemViewModel? _selected;

    /// <summary>Whether the selection is being set from loaded data rather than by the user.</summary>
    private bool _loading;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoriesViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="api">The API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    public RepositoriesViewModel(ILocalizer localizer, IServiceApiClient api, IDialogService dialogs)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    /// <summary>
    /// Raised after the node switched to another repository; the argument tells whether the repository was just added to
    /// this computer, so its current version is still to be pulled.
    /// </summary>
    public event EventHandler<bool>? Switched;

    /// <summary>Gets the repositories.</summary>
    public ObservableCollection<RepositoryItemViewModel> Items { get; } = new();

    /// <summary>Gets a value indicating whether the node has repositories at all (a Relay has none).</summary>
    public bool IsAvailable => _role != NodeRole.Relay;

    /// <summary>Gets a value indicating whether the node is an Origin, which registers repositories by their folders.</summary>
    public bool CanRegister => _role == NodeRole.Origin;

    /// <summary>Gets a value indicating whether an error is shown.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Gets a value indicating whether the Origin could not be asked for its repositories.</summary>
    public bool HasCatalogError => !string.IsNullOrEmpty(CatalogError);

    /// <summary>Gets the name of the repository the node works with, for display.</summary>
    public string ActiveText => string.IsNullOrEmpty(ActiveName) ? _localizer.Get("Repos.Unnamed") : ActiveName;

    /// <summary>
    /// Gets a value indicating whether the nodes work with different repositories and the user has not chosen to stay.
    /// </summary>
    public bool IsMismatch
        => IsAvailable &&
            !string.IsNullOrEmpty(PeerName) &&
            !string.IsNullOrEmpty(ActiveName) &&
            !string.Equals(PeerName, ActiveName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(PeerName, _dismissedPeer, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the text of the notice shown when the nodes work with different repositories.</summary>
    public string MismatchText => _localizer.Format("Repos.PeerSwitched", PeerName ?? string.Empty, ActiveText);

    /// <summary>
    /// Takes the role of the node.
    /// </summary>
    /// <param name="role">The role.</param>
    public void Configure(NodeRole role)
    {
        _role = role;
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(CanRegister));
        OnPropertyChanged(nameof(IsMismatch));
    }

    /// <summary>
    /// Tells the bindings that the texts computed from the language changed.
    /// </summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(string.Empty);
        var items = Items.Select(item => new RepositoryItemViewModel(_localizer, item.Repository)).ToArray();
        Replace(items);
    }

    /// <summary>
    /// Loads the repositories from the service.
    /// </summary>
    /// <param name="askOrigin">Whether a Workstation asks the Origin for what it offers, which needs the link to it.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task LoadAsync(bool askOrigin = true)
    {
        if (!IsAvailable)
        {
            Items.Clear();
            return;
        }

        await RunAsync(async () => Apply(await _api.GetRepositoriesAsync(askOrigin)));
    }

    /// <summary>
    /// Applies an event of the service: the repository of this node or of the node at the other end changed.
    /// </summary>
    /// <param name="repositoryEvent">The event.</param>
    public void ApplyEvent(RepositoryEvent repositoryEvent)
    {
        ArgumentNullException.ThrowIfNull(repositoryEvent);
        var switchedHere = !string.Equals(ActiveName, repositoryEvent.Repository, StringComparison.OrdinalIgnoreCase);
        ActiveName = repositoryEvent.Repository;
        PeerName = repositoryEvent.PeerRepository ?? PeerName;
        if (switchedHere)
        {
            // Made by another window or by the command line of this node: the list is read again.
            _ = LoadAsync(askOrigin: false);
        }
    }

    /// <summary>
    /// Switches to the repository the user picked in the switcher.
    /// </summary>
    /// <param name="value">The picked repository.</param>
    partial void OnSelectedChanged(RepositoryItemViewModel? value)
    {
        if (!_loading && value is { CanSwitch: true })
        {
            _ = SwitchAsync(value);
        }
    }

    /// <summary>
    /// Switches the node to a repository. A Workstation that does not keep it yet is asked for a folder first.
    /// </summary>
    /// <param name="item">The repository.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task SwitchAsync(RepositoryItemViewModel? item)
    {
        if (item is not { CanSwitch: true })
        {
            return;
        }

        var added = false;
        if (!item.IsLocal)
        {
            var folder = await ChooseFolderAsync(item.Name);
            if (folder is null)
            {
                Reselect();
                return;
            }

            if (!await RunAsync(async () => Apply(await _api.AddRepositoryAsync(item.Name, folder))))
            {
                Reselect();
                return;
            }

            added = true;
        }

        if (await RunAsync(async () => Apply(await _api.ActivateRepositoryAsync(item.Name))))
        {
            _dismissedPeer = null;
            OnPropertyChanged(nameof(IsMismatch));
            Switched?.Invoke(this, added);
        }
        else
        {
            Reselect();
        }
    }

    /// <summary>
    /// Switches this node to the repository the node at the other end works with.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task FollowPeerAsync()
    {
        if (string.IsNullOrEmpty(PeerName))
        {
            return;
        }

        var item = Items.FirstOrDefault(candidate => string.Equals(candidate.Name, PeerName, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            // Not in the list yet: the Origin is asked for what it offers.
            await LoadAsync();
            item = Items.FirstOrDefault(candidate => string.Equals(candidate.Name, PeerName, StringComparison.OrdinalIgnoreCase));
        }

        if (item is null)
        {
            ErrorMessage = _localizer.Format("Repos.PeerRepositoryUnknown", PeerName);
            return;
        }

        await SwitchAsync(item);
    }

    /// <summary>
    /// Keeps this node on its repository and hides the notice until the other node switches again.
    /// </summary>
    [RelayCommand]
    private void Stay()
    {
        _dismissedPeer = PeerName;
        OnPropertyChanged(nameof(IsMismatch));
    }

    /// <summary>
    /// Registers a Git repository on an Origin: the user picks its folder, and it is named after the folder.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (!CanRegister)
        {
            return;
        }

        var folder = await _dialogs.PickFolderAsync(_localizer.Get("Repos.PickRepository"));
        if (folder is null)
        {
            return;
        }

        var suggestion = RepositoryName.Suggest(folder);
        var name = suggestion;
        for (var number = 2; Items.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)); number++)
        {
            name = $"{suggestion}-{number}";
        }

        await RunAsync(async () => Apply(await _api.AddRepositoryAsync(name, folder)));
    }

    /// <summary>
    /// Removes a repository from the node after the user confirmed it. Its files stay where they are.
    /// </summary>
    /// <param name="item">The repository.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task RemoveAsync(RepositoryItemViewModel? item)
    {
        if (item is not { CanRemove: true } ||
            !await _dialogs.ConfirmAsync(_localizer.Get("Repos.Remove"), _localizer.Format("Repos.RemoveConfirm", item.Name, item.Location)))
        {
            return;
        }

        await RunAsync(async () => Apply(await _api.RemoveRepositoryAsync(item.Name)));

        // What the Origin offers is not part of the answer; it is asked again so the repository can be added back.
        if (_role == NodeRole.Workstation)
        {
            await LoadAsync();
        }
    }

    /// <summary>
    /// Reloads the repositories on user request.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task ReloadAsync() => LoadAsync();

    /// <summary>
    /// Asks where a Workstation should keep a repository, suggesting a folder next to the current working copy.
    /// </summary>
    /// <param name="name">The repository name.</param>
    /// <returns>The folder, or <see langword="null"/> when the user gave none.</returns>
    private async Task<string?> ChooseFolderAsync(string name)
    {
        var current = Items.FirstOrDefault(item => item.IsActive)?.Repository.Path;
        var parent = string.IsNullOrEmpty(current) ? null : Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current));
        if (!string.IsNullOrEmpty(parent))
        {
            var suggested = Path.Combine(parent, name);
            if (await _dialogs.ConfirmAsync(_localizer.Format("Repos.DownloadTitle", name), _localizer.Format("Repos.DownloadConfirm", name, suggested)))
            {
                return suggested;
            }
        }

        return await _dialogs.PickFolderAsync(_localizer.Format("Repos.PickFolder", name));
    }

    /// <summary>
    /// Shows the repositories the service answered with.
    /// </summary>
    /// <param name="list">The repositories.</param>
    private void Apply(RepositoryListDto list)
    {
        _role = list.Role;
        ActiveName = list.Active;
        PeerName = list.PeerActive ?? PeerName;
        CatalogError = list.CatalogError;

        // An answer made without asking the Origin does not know what it offers; what was known is kept.
        var known = Items.Where(item => !item.IsLocal).Select(item => item.Repository).ToArray();
        var repositories = list.Repositories.ToList();
        if (list.Role == NodeRole.Workstation && list.Repositories.All(repository => repository.IsLocal))
        {
            repositories.AddRange(known.Where(offered => repositories.All(repository => !string.Equals(repository.Name, offered.Name, StringComparison.OrdinalIgnoreCase))));
        }

        Replace(repositories.Select(repository => new RepositoryItemViewModel(_localizer, repository)).ToArray());
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(CanRegister));
        OnPropertyChanged(nameof(IsMismatch));
    }

    /// <summary>
    /// Replaces the list and selects the repository the node works with, without treating it as a choice of the user.
    /// </summary>
    /// <param name="items">The repositories.</param>
    private void Replace(IReadOnlyList<RepositoryItemViewModel> items)
    {
        _loading = true;
        try
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            Selected = Items.FirstOrDefault(item => item.IsActive);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Puts the selection back on the repository the node works with after a switch that was not made.
    /// </summary>
    private void Reselect()
    {
        _loading = true;
        try
        {
            Selected = Items.FirstOrDefault(item => item.IsActive);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Runs a service call, tracking the busy state and wording a failure.
    /// </summary>
    /// <param name="operation">The call.</param>
    /// <returns><see langword="true"/> when the call completed.</returns>
    private async Task<bool> RunAsync(Func<Task> operation)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await operation();
            return true;
        }
        catch (ServiceApiException ex)
        {
            ErrorMessage = _localizer.Describe(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            ErrorMessage = _localizer.Format("Common.ServiceUnavailable", _localizer.Describe(ex));
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }

    /// <summary>
    /// Hides the error.
    /// </summary>
    [RelayCommand]
    private void DismissError() => ErrorMessage = null;
}
