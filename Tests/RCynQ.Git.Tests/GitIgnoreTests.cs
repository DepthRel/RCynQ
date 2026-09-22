using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.Git.Manifest;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Tests for the <c>.gitignore</c> support: the rule parser and the scan of a working tree.
/// </summary>
public class GitIgnoreTests
{
    /// <summary>
    /// Verifies how single rules match paths.
    /// </summary>
    /// <param name="rules">The text of the <c>.gitignore</c> file.</param>
    /// <param name="path">The path relative to the file.</param>
    /// <param name="isDirectory">Whether the path is a directory.</param>
    /// <param name="expected"><see langword="true"/> ignored, <see langword="false"/> re-included, <see langword="null"/> no rule matches.</param>
    [Theory]
    [InlineData("*.log", "a.log", false, true)]
    [InlineData("*.log", "sub/deep/a.log", false, true)]
    [InlineData("*.log", "a.txt", false, null)]
    [InlineData("build/", "build", true, true)]
    [InlineData("build/", "build", false, null)]
    [InlineData("build/", "src/build", true, true)]
    [InlineData("/root.txt", "root.txt", false, true)]
    [InlineData("/root.txt", "sub/root.txt", false, null)]
    [InlineData("docs/*.md", "docs/a.md", false, true)]
    [InlineData("docs/*.md", "docs/sub/a.md", false, null)]
    [InlineData("docs/*.md", "x/docs/a.md", false, null)]
    [InlineData("**/temp", "temp", true, true)]
    [InlineData("**/temp", "a/b/temp", true, true)]
    [InlineData("logs/**", "logs/a.txt", false, true)]
    [InlineData("logs/**", "logs/x/y.txt", false, true)]
    [InlineData("logs/**", "logs", true, null)]
    [InlineData("a/**/b", "a/b", false, true)]
    [InlineData("a/**/b", "a/x/y/b", false, true)]
    [InlineData("a/**/b", "b", false, null)]
    [InlineData("a?.txt", "ab.txt", false, true)]
    [InlineData("a?.txt", "a.txt", false, null)]
    [InlineData("a?.txt", "a/b.txt", false, null)]
    [InlineData("[abc].txt", "b.txt", false, true)]
    [InlineData("[abc].txt", "d.txt", false, null)]
    [InlineData("[!abc].txt", "d.txt", false, true)]
    [InlineData("[!abc].txt", "a.txt", false, null)]
    [InlineData("*.log\n!keep.log", "keep.log", false, false)]
    [InlineData("*.log\n!keep.log", "other.log", false, true)]
    [InlineData("!a.txt\na.txt", "a.txt", false, true)]
    [InlineData("# a comment\n\n*.tmp", "x.tmp", false, true)]
    [InlineData("# a comment\n\n*.tmp", "# a comment", false, null)]
    [InlineData("\\#file", "#file", false, true)]
    [InlineData("\\!important", "!important", false, true)]
    [InlineData("name   ", "name", false, true)]
    [InlineData("name\\ ", "name ", false, true)]
    [InlineData("name\\ ", "name", false, null)]
    [InlineData("*.log\r\nbuild/\r\n", "a.log", false, true)]
    [InlineData("*.log\r\nbuild/\r\n", "build", true, true)]
    public void Rules_Match_As_Git_Does(string rules, string path, bool isDirectory, bool? expected)
    {
        var parsed = GitIgnoreRules.Parse(rules, ignoreCase: false);

        parsed.Match(path, isDirectory).Should().Be(expected);
    }

    /// <summary>
    /// Verifies that the case of the names matters only when the platform default says so.
    /// </summary>
    [Fact]
    public void Rules_Compare_Case_As_Configured()
    {
        GitIgnoreRules.Parse("Build/", ignoreCase: true).Match("build", isDirectory: true).Should().BeTrue();
        GitIgnoreRules.Parse("Build/", ignoreCase: false).Match("build", isDirectory: true).Should().BeNull();
    }

    /// <summary>
    /// Verifies that a file without rules is empty.
    /// </summary>
    [Fact]
    public void Rules_Of_Blank_File_Are_Empty()
    {
        GitIgnoreRules.Parse("\n# nothing\n   \n", ignoreCase: false).IsEmpty.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a scan leaves out what the <c>.gitignore</c> describes and keeps the file itself.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Scan_Skips_Ignored_Files_And_Directories()
    {
        using var dir = new TempDirectory();
        dir.Write(".gitignore", "*.log\nbuild/\nsecret.env\n");
        dir.Write("src/a.cs", "code");
        dir.Write("src/trace.log", "noise");
        dir.Write("build/out.dll", "binary");
        dir.Write("src/build/nested.txt", "also a build directory");
        dir.Write("secret.env", "KEY=1");
        dir.Write("notes.txt", "keep");

        var entries = await new DefaultFileManifestBuilder(new SnapshotFilter()).ScanAsync(dir.Path);

        entries.Select(entry => entry.Path).Should().Equal(".gitignore", "notes.txt", "src/a.cs");
    }

    /// <summary>
    /// Verifies that a deeper <c>.gitignore</c> overrides a shallower one and that an ignored directory is never entered.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Scan_Honours_Nested_Files_And_Does_Not_Enter_Ignored_Directories()
    {
        using var dir = new TempDirectory();
        dir.Write(".gitignore", "*.log\nvendor/\n");
        dir.Write("a.log", "ignored");
        dir.Write("sub/.gitignore", "!keep.log\n/local.txt\n");
        dir.Write("sub/keep.log", "re-included below sub");
        dir.Write("sub/other.log", "still ignored");
        dir.Write("sub/local.txt", "anchored to sub");
        dir.Write("sub/deeper/local.txt", "not anchored there");
        dir.Write("vendor/.gitignore", "!inner.txt\n");
        dir.Write("vendor/inner.txt", "cannot be re-included");

        var entries = await new DefaultFileManifestBuilder(new SnapshotFilter()).ScanAsync(dir.Path);

        entries.Select(entry => entry.Path).Should().Equal(".gitignore", "sub/.gitignore", "sub/deeper/local.txt", "sub/keep.log");
    }

    /// <summary>
    /// Verifies that the excluded directory names and the <c>.gitignore</c> files apply together, and that the latter can be switched off.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Scan_Combines_Excluded_Names_With_GitIgnore_And_Can_Disable_It()
    {
        using var dir = new TempDirectory();
        dir.Write(".gitignore", "*.tmp\n");
        dir.Write("a.txt", "x");
        dir.Write("a.tmp", "y");
        dir.Write("obj/b.txt", "z");

        var both = await new DefaultFileManifestBuilder(new SnapshotFilter(new[] { "obj" })).ScanAsync(dir.Path);
        var onlyNames = await new DefaultFileManifestBuilder(new SnapshotFilter(new[] { "obj" }, respectGitIgnore: false)).ScanAsync(dir.Path);

        both.Select(entry => entry.Path).Should().Equal(".gitignore", "a.txt");
        onlyNames.Select(entry => entry.Path).Should().Equal(".gitignore", "a.tmp", "a.txt");
    }

    /// <summary>
    /// Verifies that a rule added to the working tree hides the files from the sync base as well, instead of turning them into deletions.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Build_Uses_Rules_Of_The_Incoming_Tree_For_Both_Sides()
    {
        using var baseDir = new TempDirectory();
        using var work = new TempDirectory();
        baseDir.Write("keep.txt", "same");
        baseDir.Write("dist/bundle.js", "was synced before it became ignored");
        work.Write("keep.txt", "same");
        work.Write("dist/bundle.js", "rebuilt locally");
        work.Write(".gitignore", "dist/\n");
        var builder = new DefaultFileManifestBuilder(new SnapshotFilter());

        var changes = await builder.BuildAsync(baseDir.Path, work.Path);

        changes.Select(change => (change.Kind, change.Path)).Should().Equal((ChangeKind.Added, ".gitignore"));
    }

    /// <summary>
    /// Verifies that removing a rule turns the files into additions.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Build_Reports_Files_As_Added_When_A_Rule_Is_Removed()
    {
        using var baseDir = new TempDirectory();
        using var work = new TempDirectory();
        baseDir.Write(".gitignore", "dist/\n");
        work.Write(".gitignore", "");
        work.Write("dist/bundle.js", "now tracked");

        var changes = await new DefaultFileManifestBuilder(new SnapshotFilter()).BuildAsync(baseDir.Path, work.Path);

        changes.Select(change => (change.Kind, change.Path)).Should().Equal((ChangeKind.Modified, ".gitignore"), (ChangeKind.Added, "dist/bundle.js"));
    }
}
