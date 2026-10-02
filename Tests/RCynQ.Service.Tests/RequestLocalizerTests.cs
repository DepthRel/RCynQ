using FluentAssertions;
using RCynQ.Localization;
using RCynQ.Service.Localization;

namespace RCynQ.Service.Tests;

/// <summary>
/// Tests of <see cref="RequestLocalizer"/>: one localizer for every service of the process, that words the answers of
/// a request in the language of that request and nothing else.
/// </summary>
public sealed class RequestLocalizerTests
{
    /// <summary>The key of a text that differs between the languages.</summary>
    private const string Key = "Api.TokenInvalid";

    /// <summary>The English text of <see cref="Key"/>.</summary>
    private static readonly string English = TestTexts.English.Get(Key);

    /// <summary>The Russian text of <see cref="Key"/>.</summary>
    private static readonly string Russian = TestTexts.Russian.Get(Key);

    /// <summary>
    /// Verifies that the process speaks the language it was started with, and the default one when that language is
    /// unknown or not given.
    /// </summary>
    [Fact]
    public void Process_Speaks_The_Language_It_Was_Started_With()
    {
        English.Should().NotBe(Russian);

        new RequestLocalizer(LanguageCatalog.LoadEmbedded()).Get(Key).Should().Be(English);
        new RequestLocalizer(LanguageCatalog.LoadEmbedded(), "ru").Get(Key).Should().Be(Russian);
        new RequestLocalizer(LanguageCatalog.LoadEmbedded(), "xx").Get(Key).Should().Be(English);
    }

    /// <summary>
    /// Verifies that the language of a request holds until its scope ends, for the texts, the culture and the name of
    /// the language, and that scopes nest.
    /// </summary>
    [Fact]
    public void Request_Language_Holds_Until_The_Scope_Ends()
    {
        var localizer = new RequestLocalizer(LanguageCatalog.LoadEmbedded());

        using (localizer.Use("ru"))
        {
            localizer.Get(Key).Should().Be(Russian);
            localizer.Format(Key).Should().Be(Russian);
            localizer.Current.Code.Should().Be("ru");
            localizer.Culture.TwoLetterISOLanguageName.Should().Be("ru");
            using (localizer.Use("en"))
            {
                localizer.Get(Key).Should().Be(English);
            }

            localizer.Get(Key).Should().Be(Russian);
        }

        localizer.Get(Key).Should().Be(English);
        localizer.Current.Code.Should().Be("en");
    }

    /// <summary>
    /// Verifies that requests running at the same time do not see the language of one another, that the work a request
    /// starts keeps its language, and that the background work of the node keeps the language of the process.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Concurrent_Requests_Keep_Their_Own_Language()
    {
        var localizer = new RequestLocalizer(LanguageCatalog.LoadEmbedded());
        var russianEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var englishChecked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var russianRequest = Task.Run(async () =>
        {
            using (localizer.Use("ru"))
            {
                russianEntered.SetResult();
                await englishChecked.Task;

                // Work started by the request, on another thread, speaks its language too.
                return await Task.Run(() => localizer.Get(Key));
            }
        });

        await russianEntered.Task;
        var background = localizer.Get(Key);
        var englishRequest = await Task.Run(() =>
        {
            using (localizer.Use("en"))
            {
                return localizer.Get(Key);
            }
        });
        englishChecked.SetResult();

        (await russianRequest).Should().Be(Russian);
        englishRequest.Should().Be(English);
        background.Should().Be(English, "a request does not change the language of the process");
    }

    /// <summary>
    /// Verifies that switching the language changes the process, tells the listeners, and leaves a running request alone.
    /// </summary>
    [Fact]
    public void Switching_The_Language_Changes_The_Process_Only()
    {
        var localizer = new RequestLocalizer(LanguageCatalog.LoadEmbedded());
        var changes = 0;
        localizer.LanguageChanged += (_, _) => changes++;

        using (localizer.Use("en"))
        {
            localizer.SetLanguage("ru");

            localizer.Get(Key).Should().Be(English, "the request keeps the language it asked for");
        }

        localizer.Get(Key).Should().Be(Russian);
        changes.Should().Be(1);
        localizer.Languages.Select(language => language.Code).Should().Contain(new[] { "en", "ru" });
    }

    /// <summary>
    /// Verifies that a language without texts is refused instead of silently answering in another one.
    /// </summary>
    [Fact]
    public void Unknown_Request_Language_Is_Refused()
    {
        var use = () => new RequestLocalizer(LanguageCatalog.LoadEmbedded()).Use("xx");

        use.Should().Throw<ArgumentException>();
    }
}
