using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Localization;
using RCynQ.UI.Services.Api;

namespace RCynQ.UI.ViewModels;

/// <summary>
/// Base class of tab pages: loading the data once and refreshing it in the background, busy state, error and status
/// messages and safe execution of service calls.
/// </summary>
/// <remarks>
/// <para>
/// A page asks the service for its data when it is first opened (<see cref="EnsureLoadedAsync"/>). After that, opening
/// the page again costs nothing: it shows what it has. The data is reloaded when something marks it as outdated
/// (<see cref="Invalidate"/>), when an event of the service concerns the page on screen (<see cref="RefreshAsync"/>) or
/// when the user asks (<see cref="ReloadAsync"/>).
/// </para>
/// <para>
/// A background refresh never raises <see cref="IsBusy"/>, so the page stays usable; <see cref="IsBusy"/> belongs to the
/// actions of the user.
/// </para>
/// </remarks>
public abstract partial class PageViewModelBase : ObservableObject
{
    /// <summary>Whether the page has loaded its data and nothing has marked it as outdated since.</summary>
    private bool _isCurrent;

    /// <summary>The refresh that is running, or <see langword="null"/>.</summary>
    private Task? _refresh;

    /// <summary>Whether something asked for a refresh while one was running.</summary>
    private bool _refreshAgain;

    /// <summary>Whether <see cref="ErrorMessage"/> was set by a background refresh, so the next successful one may clear it.</summary>
    private bool _errorFromRefresh;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageViewModelBase"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    protected PageViewModelBase(ILocalizer localizer)
    {
        Localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <summary>
    /// Gets the localizer of the texts the page words itself.
    /// </summary>
    protected ILocalizer Localizer { get; }

    /// <summary>Whether an action of the user is running.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Whether the page is reloading its data in the background.</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>The last error message, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>The last informational message, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfo))]
    private string? _infoMessage;

    /// <summary>
    /// Gets a value indicating whether an error is shown.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Gets a value indicating whether an informational message is shown.
    /// </summary>
    public bool HasInfo => !string.IsNullOrEmpty(InfoMessage);

    /// <summary>
    /// Gets a value indicating whether the page shows data that is up to date as far as the application knows.
    /// </summary>
    public bool IsCurrent => _isCurrent;

    /// <summary>
    /// Raised when an action of the user on this page changed data that other pages show (links, patches, the working
    /// tree), so they reload it the next time they are opened.
    /// </summary>
    public event EventHandler? DataChanged;

    /// <summary>
    /// Tells the bindings that the texts computed from the language changed; pages that keep rows with computed texts
    /// refresh them too.
    /// </summary>
    public virtual void RefreshTexts() => OnPropertyChanged(string.Empty);

    /// <summary>
    /// Marks the data of the page as outdated: the page reloads it the next time it is opened.
    /// </summary>
    /// <remarks>
    /// Used for pages that are not on screen when something changes, so a change costs no request until the user looks.
    /// </remarks>
    public void Invalidate() => _isCurrent = false;

    /// <summary>
    /// Loads the page data when the page has none or it is outdated; otherwise does nothing.
    /// </summary>
    /// <remarks>
    /// Called when the page is opened, so moving between tabs asks the service nothing once a page is loaded.
    /// </remarks>
    /// <returns>A task representing the operation.</returns>
    public Task EnsureLoadedAsync() => _isCurrent ? Task.CompletedTask : RefreshAsync();

    /// <summary>
    /// Reloads the page data from the service in the background: the page stays usable and keeps showing what it has
    /// until the new data arrives, and <see cref="IsBusy"/> is not raised.
    /// </summary>
    /// <remarks>
    /// Refreshes asked for while one is running are joined into a single further run, so a burst of events costs at most
    /// two rounds of requests.
    /// </remarks>
    /// <returns>A task that completes when the page shows data at least as new as this call.</returns>
    public Task RefreshAsync()
    {
        if (_refresh is { IsCompleted: false })
        {
            _refreshAgain = true;
            return _refresh;
        }

        return _refresh = RefreshLoopAsync();
    }

    /// <summary>
    /// Reloads the page data because the user asked for it, showing the busy state meanwhile.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public async Task ReloadAsync()
    {
        IsBusy = true;
        try
        {
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Loads the page data from the service.
    /// </summary>
    /// <remarks>
    /// Implementations wrap their requests in <see cref="RunQuietAsync"/>.
    /// </remarks>
    /// <returns><see langword="true"/> when the data was loaded.</returns>
    protected abstract Task<bool> LoadAsync();

    /// <summary>
    /// Tells the other pages that an action on this page changed data they show.
    /// </summary>
    protected void NotifyDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Hides the error and informational messages.
    /// </summary>
    [RelayCommand]
    private void DismissMessages()
    {
        ErrorMessage = null;
        InfoMessage = null;
        _errorFromRefresh = false;
    }

    /// <summary>
    /// Runs an action of the user, tracking busy state and converting service failures into <see cref="ErrorMessage"/>.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <returns><see langword="true"/> when the operation completed without error.</returns>
    protected async Task<bool> RunSafeAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        IsBusy = true;
        ErrorMessage = null;
        _errorFromRefresh = false;
        try
        {
            // The operation may word a problem of its own, so only a failure replaces the message.
            var error = await CaptureErrorAsync(operation);
            if (error is not null)
            {
                ErrorMessage = error;
            }

            return error is null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Runs the requests of a background refresh, converting service failures into <see cref="ErrorMessage"/> without
    /// touching <see cref="IsBusy"/> or the messages of the last action of the user.
    /// </summary>
    /// <param name="operation">The requests.</param>
    /// <returns><see langword="true"/> when the operation completed without error.</returns>
    protected async Task<bool> RunQuietAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var error = await CaptureErrorAsync(operation);
        if (error is not null)
        {
            ErrorMessage = error;
            _errorFromRefresh = true;
            return false;
        }

        if (_errorFromRefresh)
        {
            ErrorMessage = null;
            _errorFromRefresh = false;
        }

        return true;
    }

    /// <summary>
    /// Runs <see cref="LoadAsync"/> until no further refresh was asked for meanwhile.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    private async Task RefreshLoopAsync()
    {
        IsRefreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;

                // Marked before the requests: whatever marks the page as outdated while they run is kept.
                _isCurrent = true;
                if (!await LoadAsync())
                {
                    _isCurrent = false;
                }
            }
            while (_refreshAgain);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Runs an operation and words the failure of the service, if any.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The error message, or <see langword="null"/> when the operation completed.</returns>
    private async Task<string?> CaptureErrorAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (ServiceApiException ex)
        {
            return Localizer.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            return Localizer.Format("Common.ServiceUnavailable", Localizer.Describe(ex));
        }
        catch (OperationCanceledException ex)
        {
            // A request that ran into the time limit of the HTTP client ends as a cancellation. Left alone it would leave
            // the command as an unhandled exception and close the application.
            return Localizer.Format("Common.ServiceUnavailable", Localizer.Describe(ex));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException)
        {
            return Localizer.Describe(ex);
        }
    }
}
