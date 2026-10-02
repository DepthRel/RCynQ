using CommunityToolkit.Mvvm.Input;
using RCynQ.Localization;
using RCynQ.UI.Services.About;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The window about the application: the brand, the slogan of the project page, the version, the links to the website
/// and the source code, a word on the license, and the way to report a problem.
/// </summary>
public sealed partial class AboutViewModel : DialogViewModelBase
{
    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>What the user can do with the project.</summary>
    private readonly ProjectActions _actions;

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="actions">What the user can do with the project.</param>
    public AboutViewModel(ILocalizer localizer, ProjectActions actions)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>
    /// Gets the title of the window.
    /// </summary>
    public string Title => _localizer.Get("About.Title");

    /// <summary>
    /// Gets the line with the version.
    /// </summary>
    public string Version => _localizer.Format("About.Version", AppInfo.Version);

    /// <summary>
    /// Gets the mail address for problems, questions and ideas.
    /// </summary>
    public string FeedbackEmail => ProjectSite.FEEDBACK_EMAIL;

    /// <summary>
    /// Gets the address of the project page, shown under its button.
    /// </summary>
    public string WebsiteAddress => ProjectSite.BASE_URL;

    /// <summary>
    /// Gets the address of the source code, shown under its button.
    /// </summary>
    public string SourceAddress => ProjectSite.SOURCE_URL;

    /// <summary>
    /// Opens the project page.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    [RelayCommand]
    private Task OpenWebsiteAsync() => _actions.OpenWebsiteAsync();

    /// <summary>
    /// Opens the source code.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    [RelayCommand]
    private Task OpenSourceAsync() => _actions.OpenSourceAsync();

    /// <summary>
    /// Opens the license text.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    [RelayCommand]
    private Task OpenLicenseAsync() => _actions.OpenLicenseAsync();

    /// <summary>
    /// Opens the list of components of other authors and their licenses.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    [RelayCommand]
    private Task OpenNoticesAsync() => _actions.OpenNoticesAsync();

    /// <summary>
    /// Opens a letter about a problem.
    /// </summary>
    /// <returns>A task that completes when the letter is open.</returns>
    [RelayCommand]
    private Task ReportProblemAsync() => _actions.ReportProblemAsync();

    /// <summary>
    /// Copies the mail address.
    /// </summary>
    /// <returns>A task that completes when the address is copied.</returns>
    [RelayCommand]
    private Task CopyEmailAsync() => _actions.CopyEmailAsync();

    /// <summary>
    /// Closes the window.
    /// </summary>
    [RelayCommand]
    private void CloseWindow() => Close(false);
}
