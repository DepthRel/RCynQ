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
    private const string KEY = "Api.TokenInvalid";

    /// <summary>The English text of <see cref="KEY"/>.</summary>
    private static readonly string _english = TestTexts.English.Get(KEY);

    /// <summary>The Russian text of <see cref="KEY"/>.</summary>
    private static readonly string _russian = TestTexts.Russian.Get(KEY);

    /// <summary>
    /// Verifies that the process speaks the language it was started with, and the default one when that language is
    /// unknown or not given.
    /// </summary>
    [Fact]
    public void Process_Speaks_The_Language_It_Was_Started_With()
    {
        _english.Should().NotBe(_russian);

        new RequestLocalizer(LanguageCatalog.LoadEmbedded()).Get(KEY).Should().Be(_english);
        new RequestLocalizer(LanguageCatalog.LoadEmbedded(), "ru").Get(KEY).Should().Be(_russian);
        new RequestLocalizer(LanguageCatalog.LoadEmbedded(), "xx").Get(KEY).Should().Be(_english);
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
            localizer.Get(KEY).Should().Be(_russian);
            localizer.Format(KEY).Should().Be(_russian);
            localizer.Current.Code.Should().Be("ru");
            localizer.Culture.TwoLetterISOLanguageName.Should().Be("ru");
            using (localizer.Use("en"))
            {
                localizer.Get(KEY).Should().Be(_english);
            }

            localizer.Get(KEY).Should().Be(_russian);
        }

        localizer.Get(KEY).Should().Be(_english);
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
                return await Task.Run(() => localizer.Get(KEY));
            }
        });

        await russianEntered.Task;
        var background = localizer.Get(KEY);
        var englishRequest = await Task.Run(() =>
        {
            using (localizer.Use("en"))
            {
                return localizer.Get(KEY);
            }
        });
        englishChecked.SetResult();

        (await russianRequest).Should().Be(_russian);
        englishRequest.Should().Be(_english);
        background.Should().Be(_english, "a request does not change the language of the process");
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

            localizer.Get(KEY).Should().Be(_english, "the request keeps the language it asked for");
        }

        localizer.Get(KEY).Should().Be(_russian);
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
