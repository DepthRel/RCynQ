using System.Text.RegularExpressions;

namespace RCynQ.Localization.Tests;

/// <summary>
/// Checks that the texts shown to the user name a node only by its role (Workstation, Relay, Origin) and never by the
/// working names of earlier versions or by "source" and "target".
/// </summary>
public sealed partial class RoleWordingTests
{
    /// <summary>
    /// The words that must not reach the user: the earlier names of the roles and "source" and "target" in every
    /// language of the application.
    /// </summary>
    [GeneratedRegex(@"\b(source|sources|target|targets|dest|mediator|mediators)\b|посредник|источник|приёмник|\b(Quelle|Quellen|Ziel|Vermittler|fuente|fuentes|destino|destinos|mediador|mediadores|destination|destinations|cible|cibles|médiateur|médiateurs|fonte|fontes)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenWord();

    /// <summary>
    /// The pattern of a quoted technical name, such as the name of a parameter: <c>'target'</c>.
    /// </summary>
    [GeneratedRegex(@"'[A-Za-z]+'")]
    private static partial Regex QuotedName();

    /// <summary>
    /// Lists the languages the application carries.
    /// </summary>
    /// <returns>The language codes.</returns>
    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var language in LanguageCatalog.LoadEmbedded().Languages)
        {
            data.Add(language.Code);
        }

        return data;
    }

    /// <summary>
    /// Verifies that no text of a language uses a forbidden word.
    /// </summary>
    /// <param name="code">The language code.</param>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Texts_NameNodesOnlyByTheirRoles(string code)
    {
        var pack = LanguageCatalog.LoadEmbedded().Find(code)!;

        var offending = pack.Keys
            .Where(key => pack.TryGet(key, out var text) && ForbiddenWord().IsMatch(QuotedName().Replace(text, string.Empty)))
            .Order()
            .ToArray();

        Assert.Empty(offending);
    }

    /// <summary>
    /// Verifies that no key carries the working name of a role of an earlier version.
    /// </summary>
    [Fact]
    public void Keys_DoNotCarryEarlierRoleNames()
    {
        var offending = LanguageCatalog.LoadEmbedded().Default.Keys
            .Where(key => Regex.IsMatch(key, @"(Source|Dest|Mediator)(?![a-z])"))
            .Order()
            .ToArray();

        Assert.Empty(offending);
    }
}
