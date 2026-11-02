using Avalonia;
using Avalonia.Controls;

namespace RCynQ.UI.Controls;

/// <summary>
/// The head of a page or of its section: the first child is what it tells, on the left; the other children are its
/// buttons, or one group of them, on the right. The buttons stand side by side while everything fits on one line. When it
/// does not, they stand one above the other, equally wide, and the first child gets the width that is left, so it never
/// runs under them: give it a panel that wraps.
/// </summary>
public sealed class PageHeadPanel : Panel
{
    /// <summary>The space between the buttons.</summary>
    private const double BUTTON_SPACING = 6;

    /// <summary>The free space between the first child and the buttons.</summary>
    private const double GAP = 16;

    /// <summary>Whether the last measure put the buttons one above the other.</summary>
    private bool _stacked;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var head = Children[0];
        foreach (var button in Children.Skip(1))
        {
            button.Measure(Size.Infinity);
        }

        head.Measure(Size.Infinity);
        _stacked = head.DesiredSize.Width + GAP + ButtonsSize(false).Width > availableSize.Width;

        var buttons = ButtonsSize(_stacked);
        head.Measure(availableSize.WithWidth(Math.Max(0, availableSize.Width - buttons.Width - GAP)));

        return new Size(Math.Min(availableSize.Width, head.DesiredSize.Width + GAP + buttons.Width), Math.Max(head.DesiredSize.Height, buttons.Height));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var buttons = ButtonsSize(_stacked);
        var left = finalSize.Width - buttons.Width;
        var top = (finalSize.Height - buttons.Height) / 2;
        foreach (var button in Children.Skip(1))
        {
            var size = button.DesiredSize;
            if (_stacked)
            {
                button.Arrange(new Rect(left, top, buttons.Width, size.Height));
                top += size.Height + BUTTON_SPACING;
            }
            else
            {
                button.Arrange(new Rect(left, top, size.Width, size.Height));
                left += size.Width + BUTTON_SPACING;
            }
        }

        var head = Children[0];
        head.Arrange(new Rect(0, (finalSize.Height - head.DesiredSize.Height) / 2, Math.Max(0, finalSize.Width - buttons.Width - GAP), head.DesiredSize.Height));

        return finalSize;
    }

    /// <summary>
    /// Works out the room the buttons take.
    /// </summary>
    /// <param name="stacked">Whether they stand one above the other.</param>
    /// <returns>Their common size.</returns>
    private Size ButtonsSize(bool stacked)
    {
        var buttons = Children.Skip(1).Select(button => button.DesiredSize).ToArray();
        var spacing = BUTTON_SPACING * (buttons.Length - 1);

        return stacked ?
            new Size(buttons.Max(size => size.Width), buttons.Sum(size => size.Height) + spacing) :
            new Size(buttons.Sum(size => size.Width) + spacing, buttons.Max(size => size.Height));
    }
}
