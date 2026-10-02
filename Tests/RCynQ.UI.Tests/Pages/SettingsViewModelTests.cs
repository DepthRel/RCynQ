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
    /// Creates the view model.
    /// </summary>
    /// <returns>The view model.</returns>
    private SettingsViewModel Create() => new(TestTexts.Russian, new LanguageViewModel(TestTexts.Russian, new FakeLanguageService()), _api, _dialogs);
}
