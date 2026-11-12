using Avalonia.Controls;
using FluentAssertions;
using NSubstitute;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Shell;

namespace RCynQ.UI.Tests.Shell;

/// <summary>
/// Tests of <see cref="TrayPreferences"/> and <see cref="CloseToTrayPolicy"/>: whether closing the window only hides it.
/// </summary>
public sealed class TrayPreferencesTests
{
    /// <summary>The settings substitute.</summary>
    private readonly IUiSettingsStore _settings = Substitute.For<IUiSettingsStore>();

    /// <summary>
    /// Verifies that, until the user chooses, closing to the tray is on outside Linux and off on Linux, where a desktop
    /// may show no tray icon at all.
    /// </summary>
    /// <param name="isLinux">Whether the application runs on Linux.</param>
    /// <param name="expected">The expected default.</param>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Default_Depends_On_The_Platform(bool isLinux, bool expected)
    {
        _settings.Get(TrayPreferences.SETTING).Returns((string?)null);

        new TrayPreferences(_settings, isLinux).CloseToTray.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that the choice of the user wins over the default, and that an unreadable value falls back to it.
    /// </summary>
    [Fact]
    public void Stored_Choice_Wins_And_Garbage_Falls_Back()
    {
        _settings.Get(TrayPreferences.SETTING).Returns("True");
        new TrayPreferences(_settings, isLinux: true).CloseToTray.Should().BeTrue();

        _settings.Get(TrayPreferences.SETTING).Returns("False");
        new TrayPreferences(_settings, isLinux: false).CloseToTray.Should().BeFalse();

        _settings.Get(TrayPreferences.SETTING).Returns("perhaps");
        new TrayPreferences(_settings, isLinux: false).CloseToTray.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a choice is stored under the setting.
    /// </summary>
    [Fact]
    public void Choice_Is_Stored()
    {
        var preferences = new TrayPreferences(_settings, isLinux: false);

        preferences.SetCloseToTray(false);
        preferences.SetCloseToTray(true);

        Received.InOrder(() =>
        {
            _settings.Set(TrayPreferences.SETTING, "False");
            _settings.Set(TrayPreferences.SETTING, "True");
        });
    }

    /// <summary>
    /// Verifies that only the user closing the window hides it, and only while closing to the tray is on; the end of the
    /// application or of the operating system closes it.
    /// </summary>
    /// <param name="reason">Why the window closes.</param>
    /// <param name="isProgrammatic">Whether the application closes the window itself.</param>
    /// <param name="closeToTray">Whether closing to the tray is on.</param>
    /// <param name="expected">Whether the window is hidden.</param>
    [Theory]
    [InlineData(WindowCloseReason.WindowClosing, false, true, true)]
    [InlineData(WindowCloseReason.WindowClosing, false, false, false)]
    [InlineData(WindowCloseReason.WindowClosing, true, true, false)]
    [InlineData(WindowCloseReason.ApplicationShutdown, false, true, false)]
    [InlineData(WindowCloseReason.OSShutdown, false, true, false)]
    [InlineData(WindowCloseReason.Undefined, false, true, false)]
    public void Only_The_User_Closing_The_Window_Hides_It(WindowCloseReason reason, bool isProgrammatic, bool closeToTray, bool expected)
        => CloseToTrayPolicy.ShouldHide(reason, isProgrammatic, closeToTray).Should().Be(expected);
}
