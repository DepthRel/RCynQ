using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using NSubstitute;
using RCynQ.UI.Services.About;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.Views.Dialogs;
using static RCynQ.UI.Headless.Tests.HeadlessSession;

namespace RCynQ.UI.Headless.Tests;

/// <summary>
/// Opens the real window about the application: the brand, the slogan, the links and the way to report a problem are
/// on it, in the language in use, and they follow a change of the language.
/// </summary>
public sealed class AboutDialogTests
{
    /// <summary>
    /// The window shows the brand, the slogan, the addresses and the mail of the project in English and in Russian.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Window_Shows_The_Brand_The_Links_And_The_Mail() => Value.Dispatch(
        () =>
        {
            var localizer = AppLocalizer;
            localizer.SetLanguage("en");
            var dialogs = Substitute.For<IDialogService>();
            var window = new AboutDialog { DataContext = new AboutViewModel(localizer, new ProjectActions(localizer, dialogs)) };
            window.Show();
            try
            {
                Settle();
                var english = TextsOf(window);
                Assert.Contains("Ship your code to a remote machine.", english);
                Assert.Contains("Without a commit.", english);
                Assert.Contains("Remote Code sYNchronization, Quick", english);
                Assert.Contains("Report a problem", english);
                Assert.Contains("Website", english);
                Assert.Contains("Code on GitHub", english);
                Assert.Contains("License text", english);
                Assert.Contains("Components and their licenses", english);
                Assert.Contains(ProjectSite.FEEDBACK_EMAIL, english);
                Assert.Contains(ProjectSite.BASE_URL, english);
                Assert.Contains(ProjectSite.SOURCE_URL, english);
                Assert.Equal("About RCynQ", window.Title);

                localizer.SetLanguage("ru");
                Settle();
                var russian = TextsOf(window);
                Assert.Contains("Доставьте код на удалённую машину.", russian);
                Assert.Contains("Без коммита.", russian);
                Assert.Contains("Сообщить об ошибке", russian);
                Assert.DoesNotContain("Report a problem", russian);
            }
            finally
            {
                window.Close();
                localizer.SetLanguage("en");
            }

            return 0;
        },
        CancellationToken.None);

    /// <summary>
    /// Collects every text the window shows: text blocks with their runs, and the contents of the buttons.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The texts.</returns>
    private static HashSet<string> TextsOf(Window window)
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var visual in window.GetVisualDescendants())
        {
            if (visual is TextBlock block)
            {
                if (!string.IsNullOrEmpty(block.Text))
                {
                    texts.Add(block.Text);
                }

                foreach (var run in block.Inlines?.OfType<Run>() ?? [])
                {
                    texts.Add(run.Text ?? string.Empty);
                }
            }
            else if (visual is Button { Content: string content })
            {
                texts.Add(content);
            }
        }

        return texts;
    }
}
