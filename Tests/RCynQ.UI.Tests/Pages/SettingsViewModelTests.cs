using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels;
using RCynQ.UI.ViewModels.Pages;

namespace RCynQ.UI.Tests.Pages;

/// <summary>
/// Tests of the data cleanup of <see cref="SettingsViewModel"/>.
/// </summary>
public sealed class SettingsViewModelTests
{
    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The dialog substitute.</summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>
    /// Verifies that the data is cleaned only after the user confirmed it, and that the result is reported.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Clean_Data_Asks_First_And_Reports_What_Was_Removed()
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _api.CleanDataAsync(Arg.Any<CancellationToken>()).Returns(new DataCleanupReport(3, 12, 2, 1, 4, 5 * 1024 * 1024));
        var settings = Create();

        await settings.CleanDataCommand.ExecuteAsync(null);

        await _api.Received(1).CleanDataAsync(Arg.Any<CancellationToken>());
        settings.InfoMessage.Should().Be("Удалено: патчей 3, записей истории 12, прогонов диагностики 2, резервных копий 1, временных объектов 4. Освобождено 5,0 МБ.");
        settings.HasError.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that nothing is removed when the user declines.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Declined_Confirmation_Removes_Nothing()
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var settings = Create();

        await settings.CleanDataCommand.ExecuteAsync(null);

        await _api.DidNotReceive().CleanDataAsync(Arg.Any<CancellationToken>());
        settings.InfoMessage.Should().BeNull();
    }

    /// <summary>
    /// Verifies the message of a cleanup that found nothing, and that a failure of the service is shown as an error.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Empty_Cleanup_And_Service_Failure_Are_Reported()
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _api.CleanDataAsync(Arg.Any<CancellationToken>()).Returns(new DataCleanupReport(0, 0, 0, 0, 0, 0));
        var settings = Create();

        await settings.CleanDataCommand.ExecuteAsync(null);
        settings.InfoMessage.Should().Be("Удалять нечего.");

        _api.CleanDataAsync(Arg.Any<CancellationToken>()).Returns<DataCleanupReport>(_ => throw new ServiceApiException(System.Net.HttpStatusCode.BadRequest, "the data directory is not accessible"));
        await settings.CleanDataCommand.ExecuteAsync(null);
        settings.ErrorMessage.Should().Be("the data directory is not accessible");
    }

    /// <summary>
    /// Verifies that the switch for commits shows only on an Origin, saves what the user chose, and switches back when the
    /// service could not save it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Accepting_Commits_Is_Saved_And_Switched_Back_On_Failure()
    {
        var settings = Create();
        settings.Configure(TestData.Config(Core.Enums.NodeRole.Workstation));
        settings.CanAcceptCommits.Should().BeFalse("only an Origin takes commits");

        settings.Configure(TestData.Config(Core.Enums.NodeRole.Origin));
        settings.CanAcceptCommits.Should().BeTrue();
        settings.AcceptCommits.Should().BeFalse("commits are not taken unless the user allows it");

        settings.AcceptCommits = true;
        await settings.SaveAcceptCommitsCommand.ExecuteAsync(null);
        await _api.Received(1).SetAcceptCommitsAsync(true, Arg.Any<CancellationToken>());
        settings.AcceptCommits.Should().BeTrue();

        _api.SetAcceptCommitsAsync(false, Arg.Any<CancellationToken>()).Returns(_ => throw new ServiceApiException(System.Net.HttpStatusCode.BadRequest, "cannot write"));
        settings.AcceptCommits = false;
        await settings.SaveAcceptCommitsCommand.ExecuteAsync(null);
        settings.AcceptCommits.Should().BeTrue("the service kept taking commits");
        settings.ErrorMessage.Should().Be("cannot write");
    }

    /// <summary>
    /// Creates the view model.
    /// </summary>
    /// <returns>The view model.</returns>
    private SettingsViewModel Create() => new(TestTexts.Russian, new LanguageViewModel(TestTexts.Russian, new FakeLanguageService()), _api, _dialogs);
}
