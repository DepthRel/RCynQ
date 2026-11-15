using System.Text;
using FluentAssertions;
using RCynQ.Sync.Partial;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="MyersLineDiff"/> and <see cref="FileDiff"/>: the changes of a text file and the versions that take only
/// some of them.
/// </summary>
public sealed class FileDiffTests
{
    /// <summary>
    /// Verifies that two separate edits are two changes, each with the lines it replaces.
    /// </summary>
    [Fact]
    public void Separate_Edits_Are_Separate_Changes()
    {
        var diff = FileDiff.Create(Bytes("a\nb\nc\nd\ne\n"), Bytes("a\nB\nc\nd\ne\nf\n"));

        diff.Blocks.Should().Equal(
            new DiffBlock(0, 1, 1, 1, 1),
            new DiffBlock(1, 5, 0, 5, 1));
    }

    /// <summary>
    /// Verifies that the version put together from some changes takes only those, and from all of them is the working file.
    /// </summary>
    [Fact]
    public void Compose_Takes_Only_The_Chosen_Changes()
    {
        var diff = FileDiff.Create(Bytes("a\nb\nc\nd\ne\n"), Bytes("a\nB\nc\nd\ne\nf\n"));

        Text(diff.Compose(new[] { 1 })).Should().Be("a\nb\nc\nd\ne\nf\n");
        Text(diff.Compose(new[] { 0 })).Should().Be("a\nB\nc\nd\ne\n");
        Text(diff.Compose(new[] { 0, 1 })).Should().Be("a\nB\nc\nd\ne\nf\n");
        Text(diff.Compose(Array.Empty<int>())).Should().Be("a\nb\nc\nd\ne\n");
    }

    /// <summary>
    /// Verifies that <c>\r\n</c> against <c>\n</c> is no change, and that the lines keep their own endings in a version
    /// put together from both files.
    /// </summary>
    [Fact]
    public void Line_Endings_Are_No_Change_And_Are_Kept()
    {
        var diff = FileDiff.Create(Bytes("a\r\nb\r\nc\r\n"), Bytes("a\nX\nc\n"));

        diff.Blocks.Should().ContainSingle().Which.Should().Be(new DiffBlock(0, 1, 1, 1, 1));
        Text(diff.Compose(new[] { 0 })).Should().Be("a\r\nX\nc\r\n");
    }

    /// <summary>
    /// Verifies that a last line without a line ending differs from the same line with one.
    /// </summary>
    [Fact]
    public void A_Missing_Final_Line_Ending_Is_A_Change()
    {
        var diff = FileDiff.Create(Bytes("a\nb"), Bytes("a\nb\n"));

        diff.Blocks.Should().ContainSingle().Which.Should().Be(new DiffBlock(0, 1, 1, 1, 1));
        Text(diff.Compose(Array.Empty<int>())).Should().Be("a\nb");
    }

    /// <summary>
    /// Verifies that an added file is one change, and that a file without changes has none.
    /// </summary>
    [Fact]
    public void Added_And_Unchanged_Files()
    {
        FileDiff.Create(Array.Empty<byte>(), Bytes("a\nb\n")).Blocks.Should().Equal(new DiffBlock(0, 0, 0, 0, 2));
        FileDiff.Create(Bytes("a\n"), Bytes("a\n")).Blocks.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the choice a version was put together from is found again, and that a version edited by hand is not
    /// taken for a choice.
    /// </summary>
    [Fact]
    public void FindChoice_Recognizes_Composed_Versions_Only()
    {
        var diff = FileDiff.Create(Bytes("1\n2\n3\n4\n5\n6\n7\n"), Bytes("1\nX\n3\n4\n5\nY\n7\nZ\n"));
        diff.Blocks.Should().HaveCount(3);

        diff.FindChoice(diff.Compose(new[] { 0, 2 })).Should().Equal(0, 2);
        diff.FindChoice(diff.Compose(Array.Empty<int>())).Should().BeEmpty();
        diff.FindChoice(diff.Work.Bytes).Should().Equal(0, 1, 2);
        diff.FindChoice(Bytes("1\nQ\n3\n4\n5\n6\n7\n")).Should().BeNull();
    }

    /// <summary>
    /// Verifies that the result is the shortest edit on a sequence where a naive match goes wrong.
    /// </summary>
    [Fact]
    public void Finds_The_Shortest_Edit()
    {
        var blocks = MyersLineDiff.Compare(new[] { 1, 2, 3, 1, 2, 2, 1 }, new[] { 3, 2, 1, 2, 1, 3 });

        blocks.Sum(block => block.BaseCount + block.WorkCount).Should().Be(5, "the classic example of Myers needs five edits");
    }

    /// <summary>
    /// Verifies that a rewrite with more edits than allowed is one change and is found quickly.
    /// </summary>
    [Fact]
    public void A_Large_Rewrite_Is_One_Change()
    {
        var first = Enumerable.Range(0, 5000).ToArray();
        var second = Enumerable.Range(10000, 5000).ToArray();

        var blocks = MyersLineDiff.Compare(first, second, maxEdits: 100);

        blocks.Should().Equal(new DiffBlock(0, 0, 5000, 0, 5000));
    }

    /// <summary>
    /// Verifies on many random edits that every version put together from the changes is what applying them by hand
    /// gives: all of them the working file, none of them the base.
    /// </summary>
    [Fact]
    public void Random_Edits_Round_Trip()
    {
        var random = new Random(20261115);
        for (var round = 0; round < 200; round++)
        {
            var baseLines = Enumerable.Range(0, random.Next(0, 40)).Select(_ => random.Next(0, 6).ToString()).ToList();
            var workLines = baseLines.ToList();
            for (var edit = random.Next(0, 6); edit > 0; edit--)
            {
                var at = random.Next(0, workLines.Count + 1);
                if (random.Next(2) == 0 && at < workLines.Count)
                {
                    workLines.RemoveAt(at);
                }
                else
                {
                    workLines.Insert(at, random.Next(0, 9).ToString());
                }
            }

            var baseBytes = Bytes(string.Concat(baseLines.Select(line => line + "\n")));
            var workBytes = Bytes(string.Concat(workLines.Select(line => line + "\n")));
            var diff = FileDiff.Create(baseBytes, workBytes);

            diff.Compose(diff.Blocks.Select(block => block.Index)).Should().Equal(workBytes);
            diff.Compose(Array.Empty<int>()).Should().Equal(baseBytes);
        }
    }

    /// <summary>
    /// Encodes text as UTF-8.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>
    /// Decodes UTF-8.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text.</returns>
    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
