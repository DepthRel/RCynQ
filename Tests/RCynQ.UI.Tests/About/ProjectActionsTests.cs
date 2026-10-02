using FluentAssertions;
using NSubstitute;
using RCynQ.UI.Services.About;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Pages;

namespace RCynQ.UI.Tests.About;

/// <summary>
/// Tests of the letter about a problem, of the links of the project and of the part about the application in the
/// settings.
/// </summary>
public sealed class ProjectActionsTests
{
    /// <summary>The dialog substitute.</summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>
    /// Verifies that the letter goes to the mail of the project, carries the version in the subject and the version,
    /// the system and the language in the body, in the language of the interface.
    /// </summary>
    [Fact]
    public void Letter_Is_Addressed_To_The_Project_And_Carries_The_Facts()
    {
        var letter = ProblemReport.CreateMailto(TestTexts.Russian, "1.2.3", "Windows 11 (X64)");

        letter.Scheme.Should().Be(Uri.UriSchemeMailto);
        letter.OriginalString.Should().StartWith("mailto:" + ProjectSite.FEEDBACK_EMAIL + "?subject=");
        var query = Uri.UnescapeDataString(letter.OriginalString);
        query.Should().Contain("RCynQ 1.2.3: проблема");
        query.Should().Contain("Что случилось:");
        query.Should().Contain("Версия: 1.2.3\r\nСистема: Windows 11 (X64)\r\nЯзык: ru");
    }

    /// <summary>
    /// Verifies that a letter the mail program took asks nothing more.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Opened_Letter_Asks_Nothing_More()
    {
        _dialogs.OpenUriAsync(Arg.Any<Uri>()).Returns(true);

        await new ProjectActions(TestTexts.Russian, _dialogs).ReportProblemAsync();

        await _dialogs.Received(1).OpenUriAsync(Arg.Is<Uri>(u => u.Scheme == Uri.UriSchemeMailto));
        await _dialogs.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>
    /// Verifies that without a mail program the address is offered and copied only when the user agrees.
    /// </summary>
    /// <param name="agreed">Whether the user agrees to copy the address.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Without_A_Mail_Program_The_Address_Is_Offered(bool agreed)
    {
        _dialogs.OpenUriAsync(Arg.Any<Uri>()).Returns(false);
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(agreed);

        await new ProjectActions(TestTexts.Russian, _dialogs).ReportProblemAsync();

        await _dialogs.Received(1).ConfirmAsync("Почтовая программа не открылась", Arg.Is<string>(t => t.Contains(ProjectSite.FEEDBACK_EMAIL)));
        await _dialogs.Received(agreed ? 1 : 0).CopyToClipboardAsync(ProjectSite.FEEDBACK_EMAIL);
    }

    /// <summary>
    /// Verifies that the window about the application opens the website, the source code, the license and the notices of
    /// the components over https.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task About_Opens_The_Website_And_The_Source_Code()
    {
        var about = new AboutViewModel(TestTexts.Russian, new ProjectActions(TestTexts.Russian, _dialogs));

        await about.OpenWebsiteCommand.ExecuteAsync(null);
        await about.OpenSourceCommand.ExecuteAsync(null);
        await about.OpenLicenseCommand.ExecuteAsync(null);
        await about.OpenNoticesCommand.ExecuteAsync(null);

        await _dialogs.Received(1).OpenUriAsync(new Uri(ProjectSite.BASE_URL));
        await _dialogs.Received(1).OpenUriAsync(new Uri(ProjectSite.SOURCE_URL));
        await _dialogs.Received(1).OpenUriAsync(new Uri("https://github.com/DepthRel/RCynQ/blob/main/LICENSE.txt"));
        await _dialogs.Received(1).OpenUriAsync(new Uri("https://github.com/DepthRel/RCynQ/blob/main/THIRD-PARTY-NOTICES.txt"));
        about.Title.Should().Be("О программе RCynQ");
        about.Version.Should().Be("Версия " + AppInfo.Version);
    }

    /// <summary>
    /// Verifies that the settings show the version, open the window about the application and write a letter.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Settings_Open_About_And_Report_A_Problem()
    {
        _dialogs.OpenUriAsync(Arg.Any<Uri>()).Returns(true);
        var settings = new SettingsViewModel(
            TestTexts.Russian,
            new LanguageViewModel(TestTexts.Russian, new FakeLanguageService()),
            Substitute.For<IServiceApiClient>(),
            _dialogs);

        await settings.OpenAboutCommand.ExecuteAsync(null);
        await settings.ReportProblemCommand.ExecuteAsync(null);

        settings.VersionText.Should().Be("Версия " + AppInfo.Version);
        await _dialogs.Received(1).ShowAboutAsync(Arg.Any<AboutViewModel>());
        await _dialogs.Received(1).OpenUriAsync(Arg.Is<Uri>(u => u.Scheme == Uri.UriSchemeMailto));
    }
}
