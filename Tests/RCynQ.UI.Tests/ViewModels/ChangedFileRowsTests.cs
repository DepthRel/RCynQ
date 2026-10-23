using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.Tests.ViewModels;

/// <summary>
/// Tests of <see cref="ChangedFileRows"/>: the flat list, the tree of folders and the filter of the changed files.
/// </summary>
public sealed class ChangedFileRowsTests
{
    /// <summary>The list that owns the folder rows; every folder is open unless a test closes it.</summary>
    private readonly IChangedFolderOwner _owner = Substitute.For<IChangedFolderOwner>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangedFileRowsTests"/> class.
    /// </summary>
    public ChangedFileRowsTests()
    {
        _owner.IsFolderExpanded(Arg.Any<string>()).Returns(true);
    }

    /// <summary>
    /// Verifies that the tree shows the folders before the files, each by name, joins a folder that holds only one other
    /// folder with it and indents every level.
    /// </summary>
    [Fact]
    public void Tree_Puts_Folders_First_And_Joins_Single_Folder_Chains()
    {
        var files = Files("src/app/util.cs", "README.md", "src/lib/x.cs", "docs/guide/intro.md", "src/app/Main.cs");

        var rows = ChangedFileRows.Tree(files, _owner);

        rows.Select(row => row.Label).Should().Equal("docs/guide", "intro.md", "src", "app", "Main.cs", "util.cs", "lib", "x.cs", "README.md");
        rows.Select(row => row.IndentWidth).Should().Equal(0, 38, 0, 18, 56, 56, 18, 56, 20);
        rows.OfType<ChangedFolderItemViewModel>().Select(folder => (folder.Path, folder.FileCount))
            .Should().Equal(("docs/guide", 1), ("src", 3), ("src/app", 2), ("src/lib", 1));
    }

    /// <summary>
    /// Verifies that the contents of a closed folder are left out, while the folder still counts its files.
    /// </summary>
    [Fact]
    public void Closed_Folder_Hides_Its_Contents()
    {
        _owner.IsFolderExpanded("src").Returns(false);

        var rows = ChangedFileRows.Tree(Files("src/app/a.cs", "src/b.cs", "c.txt"), _owner);

        rows.Select(row => row.Label).Should().Equal("src", "c.txt");
        rows.OfType<ChangedFolderItemViewModel>().Single().FileCount.Should().Be(2);
    }

    /// <summary>
    /// Verifies that folders whose names differ only by case stay apart, as Git keeps them.
    /// </summary>
    [Fact]
    public void Folders_That_Differ_Only_By_Case_Stay_Apart()
    {
        var rows = ChangedFileRows.Tree(Files("a/y.txt", "A/x.txt"), _owner);

        rows.OfType<ChangedFolderItemViewModel>().Select(folder => folder.Path).Should().Equal("A", "a");
    }

    /// <summary>
    /// Verifies that the flat list keeps the order of the service and shows full paths without indentation, also for
    /// rows that were laid out in the tree before.
    /// </summary>
    [Fact]
    public void Flat_List_Shows_Full_Paths_In_The_Given_Order()
    {
        var files = Files("src/b.cs", "a.txt");
        ChangedFileRows.Tree(files, _owner);

        var rows = ChangedFileRows.Flat(files);

        rows.Select(row => row.Label).Should().Equal("src/b.cs", "a.txt");
        rows.Should().OnlyContain(row => row.IndentWidth == 0);
    }

    /// <summary>
    /// Verifies that the filter ignores case and surrounding spaces, takes a backslash for the separator and passes
    /// everything when empty.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="filter">The text of the filter.</param>
    /// <param name="expected">Whether the file is shown.</param>
    [Theory]
    [InlineData("Src/App/Main.cs", "app/main", true)]
    [InlineData("src/app/main.cs", " APP\\MAIN ", true)]
    [InlineData("src/app/main.cs", "", true)]
    [InlineData("src/app/main.cs", "   ", true)]
    [InlineData("src/app/main.cs", "lib", false)]
    public void Filter_Matches_The_Path_Ignoring_Case(string path, string filter, bool expected)
        => ChangedFileRows.Matches(path, filter).Should().Be(expected);

    /// <summary>
    /// Creates rows of changed files.
    /// </summary>
    /// <param name="paths">The paths.</param>
    /// <returns>The rows.</returns>
    private static ChangedFileItemViewModel[] Files(params string[] paths)
        => paths.Select(path => new ChangedFileItemViewModel(new ChangedFile(ChangeKind.Modified, path, false))).ToArray();
}
