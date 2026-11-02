namespace RCynQ.UI.Controls;

/// <summary>
/// Works out how far a page moves to show a block that has just grown under its heading.
/// </summary>
/// <remarks>
/// The page moves as little as it can: not at all when the block fits, otherwise until its end comes into view. The
/// heading has the last word: a block taller than the page stops with its heading at the top.
/// </remarks>
public static class ScrollReveal
{
    /// <summary>The free space left between the block and the edge of the page.</summary>
    public const double MARGIN = 8;

    /// <summary>
    /// Calculates the scroll offset that shows a block.
    /// </summary>
    /// <param name="offset">The current offset.</param>
    /// <param name="viewport">The height of the visible part of the page.</param>
    /// <param name="top">Where the block starts, from the top of the page content.</param>
    /// <param name="bottom">Where the block ends, from the top of the page content.</param>
    /// <returns>The new offset; never less than the current one.</returns>
    public static double OffsetFor(double offset, double viewport, double top, double bottom)
    {
        var endInView = bottom + MARGIN - viewport;
        var headingAtTop = top - MARGIN;

        return Math.Max(offset, Math.Min(endInView, headingAtTop));
    }
}
