using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace RCynQ.UI.Controls;

/// <summary>
/// A horizontal panel that shows the leading children that fit its width and hides the rest, reporting how many
/// children are visible so the view can offer the hidden ones in an overflow menu.
/// </summary>
public sealed class OverflowStackPanel : Panel
{
    /// <summary>Defines the <see cref="Spacing"/> property.</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<OverflowStackPanel, double>(nameof(Spacing));

    /// <summary>Defines the <see cref="OverflowChanged"/> event, raised when the number of visible children changes.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> OverflowChangedEvent =
        RoutedEvent.Register<OverflowStackPanel, RoutedEventArgs>(nameof(OverflowChanged), RoutingStrategies.Bubble);

    /// <summary>
    /// Initializes static members of the <see cref="OverflowStackPanel"/> class.
    /// </summary>
    static OverflowStackPanel()
    {
        AffectsMeasure<OverflowStackPanel>(SpacingProperty);
    }

    /// <summary>
    /// Raised (asynchronously, after layout) when the number of visible children changes.
    /// </summary>
    public event EventHandler<RoutedEventArgs> OverflowChanged
    {
        add => AddHandler(OverflowChangedEvent, value);
        remove => RemoveHandler(OverflowChangedEvent, value);
    }

    /// <summary>Gets or sets the space between children.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Gets the number of leading children that are shown; -1 before the first arrange.</summary>
    public int VisibleCount { get; private set; } = -1;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        double height = 0;
        var count = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += (count > 0 ? Spacing : 0) + child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
            count++;
        }

        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        var visible = 0;
        var overflowing = false;
        foreach (var child in Children)
        {
            var width = child.DesiredSize.Width;
            var offset = visible > 0 ? x + Spacing : x;
            if (!overflowing && offset + width <= finalSize.Width + 0.5)
            {
                child.Arrange(new Rect(offset, 0, width, finalSize.Height));
                x = offset + width;
                visible++;
            }
            else
            {
                overflowing = true;
                child.Arrange(new Rect(0, 0, 0, 0));
            }
        }

        if (visible != VisibleCount)
        {
            VisibleCount = visible;
            Dispatcher.UIThread.Post(() => RaiseEvent(new RoutedEventArgs(OverflowChangedEvent, this)));
        }

        return finalSize;
    }
}
