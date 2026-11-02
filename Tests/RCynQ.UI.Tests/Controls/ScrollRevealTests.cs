using FluentAssertions;
using RCynQ.UI.Controls;

namespace RCynQ.UI.Tests.Controls;

/// <summary>
/// Tests of <see cref="ScrollReveal"/>.
/// </summary>
public sealed class ScrollRevealTests
{
    /// <summary>
    /// Verifies that a block that fits does not move the page.
    /// </summary>
    [Fact]
    public void Block_That_Fits_Leaves_The_Page_Where_It_Is()
        => ScrollReveal.OffsetFor(100, 500, 200, 400).Should().Be(100);

    /// <summary>
    /// Verifies that the page moves just far enough to show the end of the block, with the margin under it.
    /// </summary>
    [Fact]
    public void Block_Past_The_Edge_Moves_The_Page_Just_Enough()
        => ScrollReveal.OffsetFor(100, 500, 400, 650).Should().Be(650 + ScrollReveal.MARGIN - 500);

    /// <summary>
    /// Verifies that a block taller than the page keeps its heading at the top.
    /// </summary>
    [Fact]
    public void Block_Taller_Than_The_Page_Stops_At_Its_Heading()
        => ScrollReveal.OffsetFor(100, 300, 450, 1200).Should().Be(450 - ScrollReveal.MARGIN);

    /// <summary>
    /// Verifies that the page never moves back: what is above the block stays as the user left it.
    /// </summary>
    [Fact]
    public void Page_Scrolled_Past_The_Block_Stays_Where_It_Is()
        => ScrollReveal.OffsetFor(500, 300, 450, 600).Should().Be(500);
}
