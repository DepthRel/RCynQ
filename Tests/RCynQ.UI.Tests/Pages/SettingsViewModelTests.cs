using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Shell;
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
    /// Verifies that the automatic cleanup shows the days the node uses, saves a typed number at once, marks an empty box
    /// as an error without saving, and keeps only the digits of what was typed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Typed_Days_Are_Saved_And_An_Empty_Box_Is_An_Error()
    {
        var settings = Create();
        settings.Configure(TestData.Config(Core.Enums.NodeRole.Workstation) with { RetentionDays = 10 });
        settings.AutoClean.Should().BeTrue();
        settings.RetentionDaysText.Should().Be("10");
        settings.DataHintText.Should().Be(TestTexts.Russian.Format("Settings.Data.HintAuto", 10));

        settings.RetentionDaysText = "3a0";
        await Task.Yield();
        settings.RetentionDaysText.Should().Be("30", "only the digits stay");
        await _api.Received(1).SetRetentionDaysAsync(30, Arg.Any<CancellationToken>());
        settings.DataHintText.Should().StartWith(TestTexts.Russian.Format("Settings.Data.HintAuto", 30));

        _api.ClearReceivedCalls();
        settings.RetentionDaysText = string.Empty;
        settings.HasRetentionError.Should().BeTrue();
        settings.RetentionDaysText = "0";
        settings.HasRetentionError.Should().BeTrue("zero days is no period");
        await _api.DidNotReceive().SetRetentionDaysAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        settings.DataHintText.Should().StartWith(TestTexts.Russian.Format("Settings.Data.HintAuto", 30), "the node still uses the saved days");
    }

    /// <summary>
    /// Verifies that the number of days takes the form of the word the language needs: "1 дня", "21 дня", but "11 дней"
    /// in Russian, and "21 days" in English.
    /// </summary>
    /// <param name="days">The number of days.</param>
    /// <param name="russianSingular">Whether Russian takes the singular form.</param>
    /// <param name="englishSingular">Whether English takes the singular form.</param>
    [Theory]
    [InlineData(1, true, true)]
    [InlineData(5, false, false)]
    [InlineData(11, false, false)]
    [InlineData(21, true, false)]
    [InlineData(111, false, false)]
    public void Days_Take_The_Form_Of_The_Language(int days, bool russianSingular, bool englishSingular)
    {
        var config = TestData.Config(Core.Enums.NodeRole.Workstation) with { RetentionDays = days };
        var russian = Create();
        russian.Configure(config);
        var english = new SettingsViewModel(TestTexts.English, new LanguageViewModel(TestTexts.English, new FakeLanguageService()), _api, _dialogs);
        english.Configure(config);

        russian.DataHintText.Should().Be(TestTexts.Russian.Format(russianSingular ? "Settings.Data.HintAutoOne" : "Settings.Data.HintAuto", days));
        english.DataHintText.Should().Be(TestTexts.English.Format(englishSingular ? "Settings.Data.HintAutoOne" : "Settings.Data.HintAuto", days));
    }

    /// <summary>
    /// Verifies that switching the automatic cleanup off saves zero days, that switching it on again saves the default
    /// number when the box is empty, and that a value the service refused is put back.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Switching_The_Cleanup_Saves_It_And_A_Refusal_Is_Put_Back()
    {
        var settings = Create();
        settings.Configure(TestData.Config(Core.Enums.NodeRole.Workstation) with { RetentionDays = 0 });
        settings.AutoClean.Should().BeFalse();
        settings.HasRetentionError.Should().BeFalse();
        settings.DataHintText.Should().StartWith(TestTexts.Russian.Get("Settings.Data.HintManual"));

        settings.RetentionDaysText = string.Empty;
        settings.AutoClean = true;
        await Task.Yield();
        settings.RetentionDaysText.Should().Be("10");
        await _api.Received(1).SetRetentionDaysAsync(10, Arg.Any<CancellationToken>());

        settings.AutoClean = false;
        await Task.Yield();
        await _api.Received(1).SetRetentionDaysAsync(0, Arg.Any<CancellationToken>());
        settings.DataHintText.Should().StartWith(TestTexts.Russian.Get("Settings.Data.HintManual"));

        _api.SetRetentionDaysAsync(10, Arg.Any<CancellationToken>()).Returns(_ => throw new ServiceApiException(System.Net.HttpStatusCode.BadRequest, "cannot write"));
        settings.AutoClean = true;
        await Task.Yield();
        settings.AutoClean.Should().BeFalse("the node still keeps the data");
        settings.ErrorMessage.Should().Be("cannot write");
    }

    /// <summary>
    /// Creates the view model.
    /// </summary>
    /// <param name="tray">Whether closing the window keeps the application running, or <see langword="null"/> when it is not offered.</param>
    /// <returns>The view model.</returns>
    private SettingsViewModel Create(ITrayPreferences? tray = null)
        => new(TestTexts.Russian, new LanguageViewModel(TestTexts.Russian, new FakeLanguageService()), _api, _dialogs, tray: tray);

    /// <summary>
    /// Verifies that the switch of closing to the tray shows only when it is offered, stores what the user chose, and
    /// puts the value back with the reason when the settings could not be written.
    /// </summary>
    [Fact]
    public void Close_To_Tray_Is_Stored_And_A_Failed_Write_Is_Shown()
    {
        Create().HasTray.Should().BeFalse();

        var tray = Substitute.For<ITrayPreferences>();
        tray.CloseToTray.Returns(true);
        var settings = Create(tray);
        settings.HasTray.Should().BeTrue();
        settings.CloseToTray.Should().BeTrue();

        settings.CloseToTray = false;
        tray.Received(1).SetCloseToTray(false);
        settings.HasError.Should().BeFalse();

        tray.When(preferences => preferences.SetCloseToTray(Arg.Any<bool>())).Do(_ => throw new IOException("the disk is full"));
        settings.CloseToTray = false;
        settings.ErrorMessage.Should().Contain("the disk is full");
        settings.CloseToTray.Should().BeTrue("the stored value did not change");
    }
}
