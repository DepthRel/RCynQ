using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RCynQ.UI.Controls;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.Views.Pages;

/// <summary>
/// The "Роль и узлы" tab.
/// </summary>
public partial class RoleAndNodesView : UserControl
{
    /// <summary>The class of the button that is the line of a node.</summary>
    private const string NODE_LINE_CLASS = "tree-row";

    /// <summary>The line of the node being unfolded, until the page knows its new height.</summary>
    private Button? _unfoldingLine;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoleAndNodesView"/> class.
    /// </summary>
    public RoleAndNodesView()
    {
        InitializeComponent();
        PageScroll.AddHandler(Button.ClickEvent, OnPageButtonClick);
        PageScroll.ScrollChanged += OnPageScrollChanged;
    }

    /// <summary>
    /// Remembers the line of a node that is about to unfold: the button runs its command after this event.
    /// </summary>
    /// <param name="sender">The scroll viewer of the page.</param>
    /// <param name="e">The event arguments; the source is the pressed button.</param>
    private void OnPageButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { DataContext: ChainNodeItemViewModel { IsExpanded: false } } line && line.Classes.Contains(NODE_LINE_CLASS))
        {
            _unfoldingLine = line;
        }
    }

    /// <summary>
    /// Moves the page once it has grown: only then are the height of the routes and the new end of the page known.
    /// </summary>
    /// <param name="sender">The scroll viewer of the page.</param>
    /// <param name="e">The event arguments.</param>
    private void OnPageScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_unfoldingLine is not { } line || e.ExtentDelta.Y == 0)
        {
            return;
        }

        _unfoldingLine = null;
        RevealRoutes(line);
    }

    /// <summary>
    /// Moves the page so the routes of an unfolded node fit, with its line still on screen.
    /// </summary>
    /// <param name="line">The line of the node.</param>
    private void RevealRoutes(Button line)
    {
        if (line.FindAncestorOfType<Border>() is not { } row || row.TranslatePoint(default, PageScroll) is not { } start)
        {
            return;
        }

        var current = PageScroll.Offset.Y;
        var top = start.Y + current;
        PageScroll.Offset = PageScroll.Offset.WithY(ScrollReveal.OffsetFor(current, PageScroll.Viewport.Height, top, top + row.Bounds.Height));
    }
}
