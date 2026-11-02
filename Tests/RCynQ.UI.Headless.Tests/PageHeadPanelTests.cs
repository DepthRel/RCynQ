using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using NSubstitute;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.Controls;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels.Pages;
using RCynQ.UI.Views.Pages;
using static RCynQ.UI.Headless.Tests.HeadlessSession;

namespace RCynQ.UI.Headless.Tests;

/// <summary>
/// Tests of <see cref="PageHeadPanel"/>: its buttons stand side by side while there is room and one above the other when
/// there is none, and nothing runs under them.
/// </summary>
public sealed class PageHeadPanelTests
{
    /// <summary>The width of the left child of the tests.</summary>
    private const double HEAD_WIDTH = 300;

    /// <summary>The height of a button of the tests.</summary>
    private const double BUTTON_HEIGHT = 30;

    /// <summary>
    /// With room for everything the buttons stay on one line, right to the edge, each as wide as it asks to be.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Buttons_Stand_Side_By_Side_While_There_Is_Room() => Value.Dispatch(
        () =>
        {
            var (head, first, second) = Arrange(800);

            Assert.Equal(first.Bounds.Top, second.Bounds.Top);
            Assert.Equal(100, first.Bounds.Width);
            Assert.Equal(140, second.Bounds.Width);
            Assert.Equal(800, second.Bounds.Right);
            Assert.Equal(HEAD_WIDTH, head.Bounds.Width);

            return 0;
        },
        CancellationToken.None);

    /// <summary>
    /// Without room for one line the buttons stand one above the other, equally wide, and the left child keeps clear of them.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Buttons_Stack_When_One_Line_Does_Not_Fit() => Value.Dispatch(
        () =>
        {
            var (head, first, second) = Arrange(500);

            Assert.Equal(first.Bounds.Left, second.Bounds.Left);
            Assert.Equal(first.Bounds.Width, second.Bounds.Width);
            Assert.Equal(140, first.Bounds.Width);
            Assert.True(second.Bounds.Top >= first.Bounds.Bottom, "the second button stands under the first");
            Assert.True(head.Bounds.Right <= first.Bounds.Left, "the left child keeps clear of the buttons");

            return 0;
        },
        CancellationToken.None);

    /// <summary>
    /// A window as narrow as it can get, with the longest counter of the lists: in no language do the texts of the three
    /// heads of the tab run under their buttons, and the two pairs of buttons stack.
    /// </summary>
    /// <param name="language">The language.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("pt")]
    [InlineData("ru")]
    public Task Texts_Of_The_Tab_Never_Run_Under_Its_Buttons(string language) => Value.Dispatch(
        async () =>
        {
            AppLocalizer.SetLanguage(language);
            var api = Substitute.For<IServiceApiClient>();
            api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status(NodeRole.Workstation) with { ChangedFiles = new[] { new ChangedFile(ChangeKind.Modified, "a.txt", false) } });
            api.GetUnsentCommitsAsync(Arg.Any<CancellationToken>()).Returns(TestData.UnsentCommits(TestData.Commit('1', "one"), TestData.Commit('2', "two")));
            var sync = new SyncViewModel(AppLocalizer, api, Substitute.For<IDialogService>());
            await sync.RefreshAsync();
            sync.ToggleSelectionModeCommand.Execute(null);

            var window = new Window { Width = 520, Height = 460, Content = new SyncView { DataContext = sync } };
            window.Show();
            Settle();
            try
            {
                var panels = window.GetVisualDescendants().OfType<PageHeadPanel>().ToArray();
                Assert.Equal(3, panels.Length);
                foreach (var panel in panels)
                {
                    var buttons = panel.Children[1];
                    Assert.All(((WrapPanel)panel.Children[0]).Children, text => Assert.True(text.Bounds.Right <= buttons.Bounds.Left, $"{text.Bounds} runs under {buttons.Bounds}"));
                }

                foreach (var pair in panels.Where(panel => panel.Children.Count == 3))
                {
                    Assert.Equal(pair.Children[1].Bounds.Left, pair.Children[2].Bounds.Left);
                    Assert.True(pair.Children[2].Bounds.Top >= pair.Children[1].Bounds.Bottom, "the buttons stand one above the other");
                }
            }
            finally
            {
                window.Close();
                AppLocalizer.SetLanguage("en");
            }

            return 0;
        },
        CancellationToken.None);

    /// <summary>
    /// Lays out a panel with a left child and two buttons.
    /// </summary>
    /// <param name="width">The width of the panel.</param>
    /// <returns>The children.</returns>
    private static (Control Head, Control First, Control Second) Arrange(double width)
    {
        var head = new Border { Width = HEAD_WIDTH, Height = BUTTON_HEIGHT };
        var first = new Button { MinWidth = 100, Height = BUTTON_HEIGHT, HorizontalAlignment = HorizontalAlignment.Stretch };
        var second = new Button { MinWidth = 140, Height = BUTTON_HEIGHT, HorizontalAlignment = HorizontalAlignment.Stretch };
        var panel = new PageHeadPanel { Children = { head, first, second } };

        panel.Measure(new Size(width, 200));
        panel.Arrange(new Rect(0, 0, width, 200));
        return (head, first, second);
    }
}
