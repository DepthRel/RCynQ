using System.Net.Http.Json;
using FluentAssertions;
using RCynQ.Core.Api;
using RCynQ.Localization;
using RCynQ.Service.Localization;
using RCynQ.Service.Tests.Support;

namespace RCynQ.Service.Tests;

/// <summary>
/// Tests that the service words its answers in the language the client asks for.
/// </summary>
public sealed class RequestLanguageTests
{
    /// <summary>
    /// Verifies the choice among the languages of an <c>Accept-Language</c> header.
    /// </summary>
    /// <param name="header">The languages with their quality, as <c>language;quality</c> pairs separated by commas.</param>
    /// <param name="expected">The language chosen, or <see langword="null"/>.</param>
    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("ru-RU;1,en;0.8", "ru")]
    [InlineData("ja;1,ru;0.5", "ru")]
    [InlineData("de-DE;1,ru;0.5", "de")]
    [InlineData("pt-BR;0.9,es;0.8", "pt")]
    [InlineData("en-US;0.5,ru;0.9", "ru")]
    [InlineData("ja,zh", null)]
    [InlineData("ru;0", null)]
    [InlineData("", null)]
    public void Choose_Picks_The_Best_Available_Language(string header, string? expected)
    {
        var requested = header.Length == 0
            ? []
            : header.Split(',').Select(part =>
            {
                var pieces = part.Split(';');
                return (Language: (string?)pieces[0], Quality: pieces.Length > 1 ? double.Parse(pieces[1], System.Globalization.CultureInfo.InvariantCulture) : 1.0);
            }).ToArray();

        new RequestLocalizer(LanguageCatalog.LoadEmbedded()).Choose(requested).Should().Be(expected);
    }

    /// <summary>
    /// Verifies that an error of the service follows the language of the request, and that a request without a language
    /// gets the default one.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Error_Follows_The_Language_Of_The_Request()
    {
        await using var host = await ServiceTestHost.StartAsync();

        var russian = host.CreateClient(withApiToken: false);
        russian.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru");
        var english = host.CreateClient(withApiToken: false);

        (await (await russian.GetAsync(ApiRoutes.STATE)).Content.ReadFromJsonAsync<ErrorResponse>())!.Error
            .Should().Be("Локальный токен API отсутствует или неверен.");
        (await (await english.GetAsync(ApiRoutes.STATE)).Content.ReadFromJsonAsync<ErrorResponse>())!.Error
            .Should().Be("The local API token is missing or invalid.");
    }
}
