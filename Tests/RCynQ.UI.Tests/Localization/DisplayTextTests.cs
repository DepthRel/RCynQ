using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.ViewModels.Formatting;
using RCynQ.UI.ViewModels.Items;
using RCynQ.UI.ViewModels.Navigation;

namespace RCynQ.UI.Tests.Localization;

/// <summary>
/// Checks that every value of the enumerations the screens describe has its text in every language: the texts are found
/// by a key built from the name of the value, so a new value without a text would show a raw key.
/// </summary>
public sealed class DisplayTextTests
{
    /// <summary>
    /// Returns the keys the screens build from enumerations.
    /// </summary>
    /// <returns>The keys.</returns>
    private static IEnumerable<string> EnumKeys()
    {
        foreach (var kind in Enum.GetValues<LinkStatusKind>())
        {
            yield return $"Link.{kind}";
        }

        foreach (var status in Enum.GetValues<PatchStatus>())
        {
            yield return $"Patch.{status}";
        }

        foreach (var direction in Enum.GetValues<PatchDirection>())
        {
            yield return $"Direction.{direction}";
        }

        foreach (var kind in Enum.GetValues<TransportKind>())
        {
            yield return $"Transport.{kind}";
        }

        foreach (var kind in Enum.GetValues<NetworkAddressKind>())
        {
            yield return $"AddressKind.{kind}";
        }

        foreach (var warning in Enum.GetValues<EnvironmentWarning>())
        {
            yield return $"Warning.{warning}";
        }

        foreach (var role in Enum.GetValues<NodeRole>())
        {
            yield return $"Field.Path.{role}";
            yield return $"Setup.RoleHint.{role}";
        }

        foreach (var tab in Enum.GetValues<TabKey>())
        {
            yield return $"Nav.{tab}";
        }

        foreach (var mode in Enum.GetValues<EndpointMode>())
        {
            yield return $"Endpoint.{mode}.Title";
            yield return $"Endpoint.{mode}.Hint";
        }

        foreach (var status in Enum.GetValues<ServiceControllerStatus>().Where(status => status != ServiceControllerStatus.NotStarted))
        {
            yield return $"Service.{status}";
        }

        foreach (var own in new[] { true, false })
        {
            foreach (var reachable in new[] { true, false })
            {
                yield return $"Address.Status.{own}.{reachable}";
            }
        }
    }

    /// <summary>
    /// Verifies that no enumeration value lacks a text.
    /// </summary>
    /// <param name="code">The language.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void Every_Enumeration_Value_Has_A_Text(string code)
    {
        var pack = LanguageCatalog.LoadEmbedded().Find(code)!;

        EnumKeys().Where(key => !pack.TryGet(key, out _)).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a display text follows the language of the localizer.
    /// </summary>
    [Fact]
    public void Display_Text_Follows_The_Language()
    {
        using var english = Texts.Scope(new Localizer(LanguageCatalog.LoadEmbedded(), "en"));

        DisplayText.Patch(PatchStatus.Applied).Should().Be("applied");
        DisplayText.Time(null).Should().Be("never");
        DisplayText.Time(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)).Should().MatchRegex(@"^2026-01-0[123] \d\d:\d\d:05$");
    }
}
