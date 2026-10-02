using System.Globalization;
using FluentAssertions;
using NSubstitute;
using RCynQ.Localization;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Localization;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels;

namespace RCynQ.UI.Tests.Localization;

/// <summary>
/// Tests of the choice, storage and selector of the interface language.
/// </summary>
public sealed class LanguageServiceTests
{
    /// <summary>The store of the settings.</summary>
    private readonly IUiSettingsStore _settings = Substitute.For<IUiSettingsStore>();

    /// <summary>The localizer under the service; it starts in the default language.</summary>
    private readonly Localizer _localizer = new(LanguageCatalog.LoadEmbedded());

    /// <summary>
    /// Creates the service under test for an operating system language.
    /// </summary>
    /// <param name="culture">The culture of the operating system.</param>
    /// <returns>The service.</returns>
    private LanguageService Create(string culture) => new(_localizer, _settings, () => CultureInfo.GetCultureInfo(culture));

    /// <summary>
    /// Verifies that on the first start a Russian system gets Russian and the choice is stored.
    /// </summary>
    [Fact]
    public void First_Start_On_Russian_System_Selects_And_Stores_Russian()
    {
        Create("ru-RU").Initialize();

        _localizer.Current.Code.Should().Be("ru");
        _settings.Received(1).Set(LanguageService.Setting, "ru");
    }

    /// <summary>
    /// Verifies that on the first start any other system gets English.
    /// </summary>
    /// <param name="culture">The culture of the operating system.</param>
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    public void First_Start_On_Other_Systems_Selects_English(string culture)
    {
        Create(culture).Initialize();

        _localizer.Current.Code.Should().Be("en");
        _settings.Received(1).Set(LanguageService.Setting, "en");
    }

    /// <summary>
    /// Verifies that a stored language wins over the language of the system and is not detected again.
    /// </summary>
    [Fact]
    public void Stored_Language_Is_Used_And_System_Is_Not_Asked_Again()
    {
        _settings.Get(LanguageService.Setting).Returns("en");
        var service = Create("ru-RU");

        service.Initialize();

        _localizer.Current.Code.Should().Be("en");
        _settings.DidNotReceiveWithAnyArgs().Set(default!, default!);
    }

    /// <summary>
    /// Verifies that a stored language that is no longer offered is replaced by the detected one.
    /// </summary>
    [Fact]
    public void Unknown_Stored_Language_Falls_Back_To_Detection()
    {
        _settings.Get(LanguageService.Setting).Returns("xx");

        Create("ru-RU").Initialize();

        _localizer.Current.Code.Should().Be("ru");
        _settings.Received(1).Set(LanguageService.Setting, "ru");
    }

    /// <summary>
    /// Verifies that a store that cannot be written does not stop the first start.
    /// </summary>
    [Fact]
    public void First_Start_Survives_An_Unwritable_Store()
    {
        _settings.When(store => store.Set(Arg.Any<string>(), Arg.Any<string>())).Do(_ => throw new IOException("read-only"));

        Create("ru-RU").Initialize();

        _localizer.Current.Code.Should().Be("ru");
    }

    /// <summary>
    /// Verifies that selecting a language switches the texts and stores the choice.
    /// </summary>
    [Fact]
    public void Select_Switches_And_Stores()
    {
        var service = Create("en-US");

        service.Select("ru");

        _localizer.Get("Common.Cancel").Should().Be("Отмена");
        _settings.Received(1).Set(LanguageService.Setting, "ru");
        service.Languages.Select(language => language.Code).Should().Equal("en", "ru");
    }

    /// <summary>
    /// Verifies that the selector shows the language in use, with a flag for each language, and applies a pick.
    /// </summary>
    [Fact]
    public void Selector_Shows_Current_Language_And_Applies_A_Pick()
    {
        var service = new FakeLanguageService();
        var selector = new LanguageViewModel(TestTexts.Russian, service);

        selector.Selected.Code.Should().Be("en");
        selector.Options.Select(option => option.Name).Should().Equal("English", "Русский");

        selector.Selected = selector.Options[1];

        service.Selected.Should().Equal("ru");
        selector.HasError.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a failure to store the choice is reported and does not throw.
    /// </summary>
    [Fact]
    public void Selector_Reports_A_Failure_To_Store()
    {
        var service = new FakeLanguageService { SelectFailure = new IOException("disk full") };
        var selector = new LanguageViewModel(TestTexts.Russian, service);

        selector.Selected = selector.Options[1];

        selector.HasError.Should().BeTrue();
        selector.Error.Should().Contain("disk full");
    }
}
