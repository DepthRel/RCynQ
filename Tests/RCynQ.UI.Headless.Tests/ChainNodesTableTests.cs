using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels.Items;
using RCynQ.UI.ViewModels.Pages;
using RCynQ.UI.Views.Pages;
using static RCynQ.UI.Headless.Tests.HeadlessSession;

namespace RCynQ.UI.Headless.Tests;

/// <summary>
/// Opens the real "Role and nodes" tab: the line of a node unfolds wherever it is pressed, the unfolded routes come into
/// view under their line, and their table names its columns.
/// </summary>
public sealed class ChainNodesTableTests
{
    /// <summary>The width of the window of the tests.</summary>
    private const double WINDOW_WIDTH = 900;

    /// <summary>
    /// The very edges of the line of a node unfold and fold it, not only the height of its text.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task The_Edges_Of_A_Line_Toggle_The_Node() => Value.Dispatch(
        async () =>
        {
            var (page, window) = await OpenAsync(nodes: 2, routes: 2, height: 700);
            try
            {
                var node = page.Nodes[0];
                var row = RowOf(window, node);

                Click(window, row, new Point(2, 2));
                Assert.True(node.IsExpanded, "the top left corner of the row belongs to the line");

                var line = row.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("tree-row"));
                Click(window, row, new Point(row.Bounds.Width - 4, line.Bounds.Height - 2));
                Assert.False(node.IsExpanded, "the bottom right corner of the line belongs to it too");
            }
            finally
            {
                window.Close();
            }

            return 0;
        },
        CancellationToken.None);

    /// <summary>
    /// A line at the bottom of the page unfolds and the page moves just enough to show its routes.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Unfolding_Moves_The_Page_To_Show_The_Routes() => Value.Dispatch(
        async () =>
        {
            var (page, window) = await OpenAsync(nodes: 6, routes: 2, height: 420);
            try
            {
                var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First();
                scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
                Settle();
                var before = scroll.Offset.Y;

                var row = RowOf(window, page.Nodes[^1]);
                Click(window, row, new Point(40, 12));

                Assert.True(page.Nodes[^1].IsExpanded);
                Assert.True(scroll.Offset.Y > before, "the page has to move for the routes to fit");
                var top = row.TranslatePoint(default, scroll)!.Value.Y;
                Assert.True(top >= 0, $"the line stays on screen, at {top}");
                Assert.True(top + row.Bounds.Height <= scroll.Viewport.Height, "the routes fit under the line");
            }
            finally
            {
                window.Close();
            }

            return 0;
        },
        CancellationToken.None);

    /// <summary>
    /// When the routes are taller than the page, the line itself stays on screen: the page stops with it at the top.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task Routes_Taller_Than_The_Page_Keep_Their_Line_On_Screen() => Value.Dispatch(
        async () =>
        {
            var (page, window) = await OpenAsync(nodes: 3, routes: 14, height: 300);
            try
            {
                var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First();
                scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
                Settle();

                var row = RowOf(window, page.Nodes[^1]);
                Click(window, row, new Point(40, 12));

                Assert.True(page.Nodes[^1].IsExpanded);
                var top = row.TranslatePoint(default, scroll)!.Value.Y;
                Assert.InRange(top, 0, 16);
                Assert.True(row.Bounds.Height > scroll.Viewport.Height, "the case of the test: the routes do not fit");
            }
            finally
            {
                window.Close();
            }

            return 0;
        },
        CancellationToken.None);

    /// <summary>
    /// The mark of the route in use stands under a heading of its own and apart from the state of the route.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public Task The_Mark_Of_The_Route_In_Use_Has_A_Column() => Value.Dispatch(
        async () =>
        {
            AppLocalizer.SetLanguage("ru");
            var (page, window) = await OpenAsync(nodes: 1, routes: 2, height: 700);
            try
            {
                page.Nodes[0].IsExpanded = true;
                Settle();

                var heading = TextOf(window, AppLocalizer.Get("Routes.Usage"));
                var mark = TextOf(window, AppLocalizer.Get("Routes.Active"));
                var state = TextOf(window, AppLocalizer.Get("Routes.Unreachable"));
                var stateHeading = TextOf(window, AppLocalizer.Get("Common.Status"));

                Assert.Equal(LeftOf(heading, window), LeftOf(mark, window), 1);
                Assert.Equal(LeftOf(stateHeading, window), LeftOf(state, window), 1);
                Assert.True(mark.DesiredSize.Width <= mark.Bounds.Width + 0.5, "the mark fits its column");
                Assert.True(LeftOf(state, window) - (LeftOf(mark, window) + mark.Bounds.Width) >= 12, "the columns stand apart");
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
    /// Opens the tab with nodes reachable in several ways.
    /// </summary>
    /// <param name="nodes">The number of nodes.</param>
    /// <param name="routes">The number of routes of each node.</param>
    /// <param name="height">The height of the window.</param>
    /// <returns>The page and its window.</returns>
    private static async Task<(RoleAndNodesViewModel Page, Window Window)> OpenAsync(int nodes, int routes, double height)
    {
        var links = Enumerable.Range(1, nodes).Select(i => TestData.Link($"link-{i}", $"node-{i}", NodeRole.Relay, $"10.0.0.{i}")).ToArray();
        var api = Substitute.For<IServiceApiClient>();
        api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Workstation));
        api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(links);
        api.GetLinkStatusAsync(Arg.Any<CancellationToken>()).Returns(links.Select(link => TestData.State(link, LinkStatus.Ready)).ToArray());

        var page = new RoleAndNodesViewModel(AppLocalizer, api, Substitute.For<IDialogService>());
        await page.RefreshAsync();
        foreach (var node in page.Nodes)
        {
            node.ApplyRoutes(RoutesOf(node, routes));
        }

        var window = new Window { Width = WINDOW_WIDTH, Height = height, Content = new RoleAndNodesView { DataContext = page } };
        window.Show();
        Settle();
        return (page, window);
    }

    /// <summary>
    /// Builds the routes of a node: the first one does not answer, the last one is in use.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="count">The number of routes.</param>
    /// <returns>The routes.</returns>
    private static LinkRoutesDto RoutesOf(ChainNodeItemViewModel node, int count) => new(
        node.LinkId,
        null,
        Enumerable.Range(0, count)
            .Select(i => new LinkRouteDto(
                Guid.NewGuid(),
                $"route-{i}",
                $"10.1.0.{i}",
                22,
                "rcynq",
                i,
                i == count - 1,
                i == 0 ? RouteReachability.Unreachable : RouteReachability.Reachable,
                i == 0 ? null : 12,
                null))
            .ToArray());

    /// <summary>
    /// Finds the row of a node.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="node">The node.</param>
    /// <returns>The row.</returns>
    private static Border RowOf(Window window, ChainNodeItemViewModel node)
        => window.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("row") && ReferenceEquals(border.DataContext, node));

    /// <summary>
    /// Finds the text on screen.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="text">The text.</param>
    /// <returns>The first text block showing it.</returns>
    private static TextBlock TextOf(Window window, string text)
        => window.GetVisualDescendants().OfType<TextBlock>().First(block => block.IsEffectivelyVisible && block.Text == text);

    /// <summary>
    /// Measures where a control starts.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="window">The window.</param>
    /// <returns>The distance from the left edge of the window.</returns>
    private static double LeftOf(Control control, Window window) => control.TranslatePoint(default, window)!.Value.X;

    /// <summary>
    /// Presses the left mouse button at a point of a control and lets the page react.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="control">The control.</param>
    /// <param name="point">The point, relative to the control.</param>
    private static void Click(Window window, Control control, Point point)
    {
        var position = control.TranslatePoint(point, window)!.Value;
        window.MouseDown(position, MouseButton.Left);
        window.MouseUp(position, MouseButton.Left);
        Settle();
    }
}
