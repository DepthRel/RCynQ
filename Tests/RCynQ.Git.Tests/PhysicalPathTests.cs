using System.Diagnostics;
using FluentAssertions;
using RCynQ.Git.Paths;

namespace RCynQ.Git.Tests;

/// <summary>
/// Tests of <see cref="PhysicalPath"/> and of <see cref="PathUtility.HasLinkedParent"/> on real directory links: a
/// symbolic link, or on Windows without the privilege for one, a junction.
/// </summary>
public sealed class PhysicalPathTests : IDisposable
{
    /// <summary>The temporary root.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-physical", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The links the test created, removed before the directories.</summary>
    private readonly List<string> _links = new();

    /// <summary>
    /// Verifies that a path without links resolves to itself, also through segments that do not exist.
    /// </summary>
    [Fact]
    public void A_Path_Without_Links_Resolves_To_Itself()
    {
        var root = PhysicalPath.Resolve(_root)!;

        PhysicalPath.Resolve(Path.Combine(_root, "a", "b"))
            .Should().Be(Path.Combine(root, "a", "b"));
        PhysicalPath.Resolve(Path.Combine(_root, "missing", "..", "a"))
            .Should().Be(Path.Combine(root, "a"));
    }

    /// <summary>
    /// Verifies that <c>..</c> after a link leaves the directory the link points to, not the one it lies in, which is
    /// where a check by the text alone goes wrong.
    /// </summary>
    [Fact]
    public void Parent_Of_A_Link_Is_The_Parent_Of_Its_Target()
    {
        var deep = Directory.CreateDirectory(Path.Combine(_root, "real", "deep")).FullName;
        var link = Path.Combine(_root, "tree", "j");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        CreateDirectoryLink(link, deep);
        var root = PhysicalPath.Resolve(_root)!;

        var lexical = Path.GetFullPath(Path.Combine(link, ".."));
        var physical = PhysicalPath.Resolve(Path.Combine(link, ".."));

        lexical.Should().Be(Path.Combine(_root, "tree"));
        physical.Should().Be(Path.Combine(root, "real"));
    }

    /// <summary>
    /// Verifies that a loop of links ends in <see langword="null"/> instead of running forever.
    /// </summary>
    [Fact]
    public void A_Loop_Of_Links_Is_Not_Followed_Forever()
    {
        var first = Path.Combine(_root, "first");
        var second = Path.Combine(_root, "second");
        CreateDirectoryLink(first, second);
        CreateDirectoryLink(second, first);

        PhysicalPath.Resolve(Path.Combine(first, "file.txt")).Should().BeNull();
    }

    /// <summary>
    /// Verifies that a link above a path is found, also when the directory it points to does not exist.
    /// </summary>
    [Fact]
    public void A_Linked_Parent_Is_Found_Even_When_Its_Target_Is_Missing()
    {
        var tree = Directory.CreateDirectory(Path.Combine(_root, "tree")).FullName;
        CreateDirectoryLink(Path.Combine(tree, "dangling"), Path.Combine(_root, "gone"));
        Directory.CreateDirectory(Path.Combine(tree, "plain"));

        PathUtility.HasLinkedParent(tree, Path.Combine(tree, "dangling", "x.txt")).Should().BeTrue();
        PathUtility.HasLinkedParent(tree, Path.Combine(tree, "plain", "x.txt")).Should().BeFalse();
        PhysicalPath.LinkTargetOf(Path.Combine(tree, "dangling")).Should().NotBeNull();
        PhysicalPath.LinkTargetOf(Path.Combine(tree, "plain")).Should().BeNull();
    }

    /// <summary>
    /// Verifies that Git metadata is recognised at any depth below a root.
    /// </summary>
    [Fact]
    public void Git_Metadata_Is_Recognised_At_Any_Depth()
    {
        PathUtility.IsInGitMetadata(_root, Path.Combine(_root, ".git")).Should().BeTrue();
        PathUtility.IsInGitMetadata(_root, Path.Combine(_root, "sub", ".GIT", "config")).Should().BeTrue();
        PathUtility.IsInGitMetadata(_root, Path.Combine(_root, "git~1")).Should().BeTrue();
        PathUtility.IsInGitMetadata(_root, Path.Combine(_root, ".gitignore")).Should().BeFalse();
        PathUtility.IsInGitMetadata(_root, _root).Should().BeFalse();
    }

    /// <summary>
    /// Removes the links first, so the cleanup never follows one, then the temporary directories.
    /// </summary>
    public void Dispose()
    {
        try
        {
            foreach (var link in _links)
            {
                if (PhysicalPath.LinkTargetOf(link) is null)
                {
                    continue;
                }

                // A link whose target is missing is no directory for the file system and goes as a file.
                if (new DirectoryInfo(link).Exists)
                {
                    Directory.Delete(link);
                }
                else
                {
                    File.Delete(link);
                }
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Creates a link to a directory, which need not exist: a symbolic link, or on Windows without the privilege for one, a
    /// junction.
    /// </summary>
    /// <param name="link">The link to create.</param>
    /// <param name="target">The directory to point to.</param>
    private void CreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is IOException or UnauthorizedAccessException)
        {
            var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            process?.WaitForExit(10_000);
        }

        PhysicalPath.LinkTargetOf(link).Should().NotBeNull("the test needs a link to a directory");
        _links.Add(link);
    }
}
