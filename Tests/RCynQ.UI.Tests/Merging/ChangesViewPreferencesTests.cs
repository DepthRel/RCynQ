using FluentAssertions;
using NSubstitute;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Merging;

namespace RCynQ.UI.Tests.Merging;

/// <summary>
/// Tests of <see cref="ChangesViewPreferences"/>.
/// </summary>
public sealed class ChangesViewPreferencesTests
{
    /// <summary>
    /// Verifies that the window of RCynQ is used until the user chooses, that a choice is kept, and that a damaged value
    /// falls back to the window of RCynQ.
    /// </summary>
    [Fact]
    public void Mode_Is_Kept_And_Defaults_To_RCynQ()
    {
        var stored = new Dictionary<string, string>();
        var settings = Substitute.For<IUiSettingsStore>();
        settings.Get(Arg.Any<string>()).Returns(call => stored.GetValueOrDefault(call.Arg<string>()));
        settings.When(store => store.Set(Arg.Any<string>(), Arg.Any<string>())).Do(call => stored[call.ArgAt<string>(0)] = call.ArgAt<string>(1));
        var preferences = new ChangesViewPreferences(settings);

        preferences.Mode.Should().Be(ChangesViewMode.BuiltIn);
        preferences.SetMode(ChangesViewMode.ExternalTool);
        new ChangesViewPreferences(settings).Mode.Should().Be(ChangesViewMode.ExternalTool);
        stored[ChangesViewPreferences.SETTING] = "42";
        preferences.Mode.Should().Be(ChangesViewMode.BuiltIn);
    }
}
