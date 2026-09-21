using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Events;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels;
using RCynQ.UI.ViewModels.Navigation;
using RCynQ.UI.ViewModels.Pages;
using RCynQ.UI.ViewModels.Setup;
using RCynQ.UI.Views;

namespace RCynQ.UI.Headless.Tests;

/// <summary>
/// Builds the application for the headless platform.
/// </summary>
public static class TestApp
{
    /// <summary>
    /// Configures Avalonia without a screen.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Opens the real main window and switches the language while every tab is shown: no text of the previous language may
/// stay on the screen. A page, a row or a status line that does not refresh with the language fails here.
/// </summary>
public sealed partial class LanguageRefreshTests
{
    /// <summary>
    /// The headless session shared by the tests (Avalonia starts once per process).
    /// </summary>
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

    /// <summary>
    /// Finds Cyrillic letters.
    /// </summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("[Ѐ-ӿ]")]
    private static partial Regex Cyrillic();

    /// <summary>
    /// Every tab shows only the texts of the language in use, before and after the language is switched.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Every_Tab_Follows_The_Language() => Session.Value.Dispatch(
        async () =>
        {
            var localizer = Texts.Localizer;
            var catalog = LanguageCatalog.LoadEmbedded();
            var english = TextsOf(catalog, "en");
            var russian = TextsOf(catalog, "ru");
            localizer.SetLanguage("en");

            var (main, window) = await OpenAsync();
            var stale = new List<string>();
            try
            {
                foreach (var tab in main.Tabs.Where(tab => tab.IsAvailable).ToArray())
                {
                    main.SelectedTab = tab;
                    Settle();
                    Check(window, $"{tab.Key}/en", english, russian, stale, requireNoCyrillic: true);

                    localizer.SetLanguage("ru");
                    Settle();
                    Check(window, $"{tab.Key}/ru", russian, english, stale, requireNoCyrillic: false);

                    localizer.SetLanguage("en");
                    Settle();
                    Check(window, $"{tab.Key}/en again", english, russian, stale, requireNoCyrillic: true);
                }
            }
            finally
            {
                window.Close();
                localizer.SetLanguage("en");
            }

            Assert.Empty(stale);
            return 0;
        },
        CancellationToken.None);

    /// <summary>
    /// The first-run setup, which has no tabs, follows the language too.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Setup_Follows_The_Language() => Session.Value.Dispatch(
        async () =>
        {
            var localizer = Texts.Localizer;
            var catalog = LanguageCatalog.LoadEmbedded();
            var english = TextsOf(catalog, "en");
            var russian = TextsOf(catalog, "ru");
            localizer.SetLanguage("en");

            var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-headless-tests", Guid.NewGuid().ToString("N"))).FullName;
            var (main, window) = await OpenAsync(Path.Combine(root, "syncbridge.json"));
            var stale = new List<string>();
            try
            {
                Assert.True(main.IsSetupVisible);
                Check(window, "setup/en", english, russian, stale, requireNoCyrillic: true);

                localizer.SetLanguage("ru");
                Settle();
                Check(window, "setup/ru", russian, english, stale, requireNoCyrillic: false);
                Assert.Equal("RCynQ — настройка узла", main.NodeTitle);
            }
            finally
            {
                window.Close();
                localizer.SetLanguage("en");
                Directory.Delete(root, recursive: true);
            }

            Assert.Empty(stale);
            return 0;
        },
        CancellationToken.None);

    /// <summary>
    /// Lets the pending bindings and layout passes run.
    /// </summary>
    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Returns the texts of a language without the names of the languages themselves, which are shown in their own language.
    /// </summary>
    /// <param name="catalog">The languages.</param>
    /// <param name="code">The language.</param>
    /// <returns>The texts by key.</returns>
    private static Dictionary<string, string> TextsOf(LanguageCatalog catalog, string code)
    {
        var pack = catalog.Find(code)!;
        return pack.Keys
            .Where(key => key is not (LanguagePack.CodeKey or LanguagePack.NameKey))
            .ToDictionary(key => key, key => pack.TryGet(key, out var text) ? text : string.Empty);
    }

    /// <summary>
    /// Collects the texts shown on the window and records the ones that belong to the other language.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="where">Where the check runs, for the report.</param>
    /// <param name="current">The texts of the language in use.</param>
    /// <param name="other">The texts of the other language.</param>
    /// <param name="stale">Receives the leftovers.</param>
    /// <param name="requireNoCyrillic">Whether Cyrillic letters count as leftovers (English is expected).</param>
    private static void Check(Window window, string where, Dictionary<string, string> current, Dictionary<string, string> other, List<string> stale, bool requireNoCyrillic)
    {
        // A text is stale when it is a text of the other language that the language in use words differently.
        var foreign = other
            .Where(pair => pair.Value.Trim().Length > 1 && current.TryGetValue(pair.Key, out var own) && own != pair.Value)
            .Select(pair => pair.Value.Trim())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var text in VisibleTexts(window))
        {
            if (foreign.Contains(text.Trim()))
            {
                stale.Add($"{where}: '{text}'");
            }
            else if (requireNoCyrillic && Cyrillic().IsMatch(text))
            {
                stale.Add($"{where}: '{text}' (Cyrillic)");
            }
        }
    }

    /// <summary>
    /// Lists the texts of the labels, buttons and check boxes that are on the screen.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The texts.</returns>
    private static IEnumerable<string> VisibleTexts(Window window)
    {
        foreach (var visual in window.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible))
        {
            switch (visual)
            {
                case TextBlock { Text: { Length: > 0 } text }:
                    yield return text;
                    break;
                case ContentControl { Content: string content } when content.Length > 0:
                    yield return content;
                    break;
            }
        }
    }

    /// <summary>
    /// Opens the main window over a service double that answers with data for every tab.
    /// </summary>
    /// <param name="configPath">The configuration file; a file with a Source node is written when it is <see langword="null"/>.</param>
    /// <returns>The view model and the window.</returns>
    private static async Task<(MainViewModel Main, MainWindow Window)> OpenAsync(string? configPath = null)
    {
        var api = Substitute.For<IServiceApiClient>();
        var events = Substitute.For<INodeEventStream>();
        var controller = Substitute.For<IServiceController>();
        var dialogs = Substitute.For<IDialogService>();

        var mediator = TestData.Link("relay", "mediator-1", NodeRole.Mediator, "203.0.113.4", TransportKind.AmneziaWG);
        var dest = TestData.Link("dest", "dest-1", NodeRole.Dest, "203.0.113.5");
        api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Source));
        api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status());
        api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(new[] { mediator, dest });
        api.GetLinkStatusAsync(Arg.Any<CancellationToken>()).Returns(new[] { TestData.State(mediator, LinkStatus.Ready), TestData.State(dest, LinkStatus.Failed) });
        api.GetPatchesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new[] { TestData.ConflictPatch() });
        api.GetBranchAsync(Arg.Any<CancellationToken>()).Returns(new GitBranchInfo(true, "main", "refs/remotes/dest/main", 1, 2));
        api.GetLogsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new LogEntryDto(DateTimeOffset.UtcNow, "Information", "started", null) });

        var configFile = configPath;
        if (configFile is null)
        {
            var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-headless-tests", Guid.NewGuid().ToString("N"))).FullName;
            configFile = Path.Combine(directory, "syncbridge.json");
            await File.WriteAllTextAsync(configFile, """{ "SyncBridge": { "NodeId": "node-1", "Role": "Source", "WorkdirPath": "/home/dev/projects/app" } }""");
        }

        controller.ConfigPath.Returns(configFile);
        controller.EnsureRunningAsync(Arg.Any<CancellationToken>()).Returns(new ServiceStartResult(ServiceStartOutcome.AlreadyRunning, "running"));

        var language = new LanguageViewModel(new FakeLanguageService());
        var main = new MainViewModel(
            api,
            events,
            new ImmediateUiDispatcher(),
            new RoleAdapter(),
            controller,
            new SetupViewModel(configFile, dialogs, language),
            new OverviewViewModel(api),
            new RoleAndNodesViewModel(api, dialogs),
            new SyncViewModel(api, dialogs),
            new TunnelViewModel(api),
            new DiagnosticsViewModel(api, dialogs),
            new BranchViewModel(api),
            new LogsViewModel(api, dialogs),
            new SettingsViewModel(language, configFile));
        await main.InitializeAsync();

        // A finished check with a failed result fills the diagnostics tab and the problems of the overview.
        var failed = new DiagnosticResult("GitInstalled", false, "git not found", "install git");
        main.Diagnostics.ApplyDiagnostics(new DiagnosticsEvent(NodeRole.Source, [failed], DateTimeOffset.UtcNow));

        var window = new MainWindow { DataContext = main };
        window.Show();
        Settle();
        return (main, window);
    }
}
