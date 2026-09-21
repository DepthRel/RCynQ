using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Localization;
using RCynQ.UI.Services.Api;

namespace RCynQ.UI.ViewModels;

/// <summary>
/// Base class of tab pages: busy state, error and status messages and safe execution of service calls.
/// </summary>
public abstract partial class PageViewModelBase : ObservableObject
{
    /// <summary>Whether an operation is running.</summary>
    [ObservableProperty]
    private bool _isBusy;

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
    /// Tells the bindings that the texts computed from the language changed; pages that keep rows with computed texts
    /// refresh them too.
    /// </summary>
    public virtual void RefreshTexts() => OnPropertyChanged(string.Empty);

    /// <summary>
    /// Reloads the page data from the service.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public abstract Task RefreshAsync();

    /// <summary>
    /// Hides the error and informational messages.
    /// </summary>
    [RelayCommand]
    private void DismissMessages()
    {
        ErrorMessage = null;
        InfoMessage = null;
    }

    /// <summary>
    /// Runs an operation, tracking busy state and converting service failures into <see cref="ErrorMessage"/>.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <returns><see langword="true"/> when the operation completed without error.</returns>
    protected async Task<bool> RunSafeAsync(Func<Task> operation)
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
            ErrorMessage = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = Texts.Format("Common.ServiceUnavailable", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }
}
