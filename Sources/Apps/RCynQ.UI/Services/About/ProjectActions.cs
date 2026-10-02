using RCynQ.Localization;
using RCynQ.UI.Services.Dialogs;

namespace RCynQ.UI.Services.About;

/// <summary>
/// What the user can do with the project from the application: open its website or its source code, write a letter
/// about a problem, copy the mail address. Shared by the settings page and the window about the application.
/// </summary>
public sealed class ProjectActions
{
    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The dialog service that opens addresses and copies text.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectActions"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="dialogs">The dialog service that opens addresses and copies text.</param>
    public ProjectActions(ILocalizer localizer, IDialogService dialogs)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    /// <summary>
    /// Opens the project page in the browser.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    public Task OpenWebsiteAsync() => _dialogs.OpenUriAsync(new Uri(ProjectSite.BASE_URL));

    /// <summary>
    /// Opens the source code in the browser.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    public Task OpenSourceAsync() => _dialogs.OpenUriAsync(new Uri(ProjectSite.SOURCE_URL));

    /// <summary>
    /// Opens the license text of RCynQ in the browser.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    public Task OpenLicenseAsync() => _dialogs.OpenUriAsync(new Uri(ProjectSite.LICENSE_URL));

    /// <summary>
    /// Opens the list of components of other authors and their licenses in the browser.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    public Task OpenNoticesAsync() => _dialogs.OpenUriAsync(new Uri(ProjectSite.NOTICES_URL));

    /// <summary>
    /// Opens a letter about a problem in the mail program of the user. When no mail program takes it, offers to copy
    /// the address, so the user can write from any other mail.
    /// </summary>
    /// <returns>A task that completes when the letter is open or the user answered.</returns>
    public async Task ReportProblemAsync()
    {
        var letter = ProblemReport.CreateMailto(_localizer, AppInfo.Version, AppInfo.System);
        if (await _dialogs.OpenUriAsync(letter))
        {
            return;
        }

        var title = _localizer.Get("Problem.NoMail.Title");
        var text = _localizer.Format("Problem.NoMail.Text", ProjectSite.FEEDBACK_EMAIL);
        if (await _dialogs.ConfirmAsync(title, text))
        {
            await CopyEmailAsync();
        }
    }

    /// <summary>
    /// Copies the mail address for problems and ideas.
    /// </summary>
    /// <returns>A task that completes when the address is copied.</returns>
    public Task CopyEmailAsync() => _dialogs.CopyToClipboardAsync(ProjectSite.FEEDBACK_EMAIL);
}
