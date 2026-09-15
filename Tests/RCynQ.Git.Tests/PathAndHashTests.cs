using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Git.Hashing;
using RCynQ.Git.Manifest;
using RCynQ.Git.Paths;
using RCynQ.Git.Tests.Support;
using Xunit;

namespace RCynQ.Git.Tests;

/// <summary>
/// Tests for path safety, state hashing and manifest building.
/// </summary>
public class PathAndHashTests
{
    /// <summary>
    /// A valid digest.
    /// </summary>
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>
    /// Another valid digest.
    /// </summary>
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    /// <summary>
    /// The utility under test.
    /// </summary>
    private readonly PathUtility _paths = new();

    /// <summary>
    /// Verifies normalization of acceptable relative paths.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="expected">The expected normalized path.</param>
    [Theory]
    [InlineData("src/a.cs", "src/a.cs")]
    [InlineData("src\\sub\\b.cs", "src/sub/b.cs")]
    [InlineData("./src//c.cs", "src/c.cs")]
    [InlineData(".gitignore", ".gitignore")]
    [InlineData("docs/.github/x.yml", "docs/.github/x.yml")]
    public void Normalize_Accepts_Safe_Paths(string input, string expected)
    {
        _paths.NormalizeRelativePath(input).Should().Be(expected);
    }

    /// <summary>
    /// Verifies rejection of traversal, rooted paths and Git metadata in all spellings.
    /// </summary>
    /// <param name="input">The unsafe input.</param>
    [Theory]
    [InlineData("../x")]
    [InlineData("a/../../x")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\x")]
    [InlineData("C:x")]
    [InlineData("file.txt:stream")]
    [InlineData(".git/hooks/pre-commit")]
    [InlineData("sub/.GIT/config")]
    [InlineData("GIT~1/config")]
    [InlineData(".git./hooks/x")]
    [InlineData("dir /x")]
    [InlineData("a\u0000b")]
    [InlineData(".")]
    public void Normalize_Rejects_Unsafe_Paths(string input)
    {
        var normalize = () => _paths.NormalizeRelativePath(input);
        normalize.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies root containment checks, including the sibling-prefix trap.
    /// </summary>
    [Fact]
    public void Root_Containment()
    {
        var root = Path.Combine(Path.GetTempPath(), "rcynq-root");
        _paths.IsWithinRoot(root, Path.Combine(root, "a", "b")).Should().BeTrue();
        _paths.IsWithinRoot(root, root + "2").Should().BeFalse();
        _paths.IsWithinRoot(root, Path.Combine(root, "..", "x")).Should().BeFalse();
        _paths.ResolveWithinRoot(root, "a/b.txt").Should().Be(Path.Combine(root, "a", "b.txt"));
    }

    /// <summary>
    /// Verifies that the state hash is deterministic and sensitive to every field.
    /// </summary>
    [Fact]
    public void StateHash_Is_Deterministic()
    {
        var hasher = new DefaultStateHasher();
        var a = new FileEntry("a.txt", HashA, 1, false);
        var b = new FileEntry("b/c.txt", HashB, 2, false);

        var hash = hasher.ComputeHash(new[] { a, b });
        hasher.ComputeHash(new[] { b, a }).Should().Be(hash);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");

        hasher.ComputeHash(new[] { a, new FileEntry("b/c.txt", HashA, 2, false) }).Should().NotBe(hash);
        hasher.ComputeHash(new[] { a, new FileEntry("b/c.txt", HashB, 3, false) }).Should().NotBe(hash);
        hasher.ComputeHash(new[] { a, new FileEntry("b/c.txt", HashB, 2, true) }).Should().NotBe(hash);
        hasher.ComputeHash(new[] { a, new FileEntry("b/d.txt", HashB, 2, false) }).Should().NotBe(hash);
        hasher.ComputeHash(Array.Empty<FileEntry>()).Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

        var duplicate = () => hasher.ComputeHash(new[] { a, a });
        duplicate.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies manifest detection of added, modified and deleted files with exclusions.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Manifest_Detects_Changes_And_Honors_Exclusions()
    {
        using var baseDir = new TempDirectory();
        using var incoming = new TempDirectory();
        baseDir.Write("same.txt", "same");
        baseDir.Write("mod.txt", "old");
        baseDir.Write("gone/del.txt", "bye");
        incoming.Write("same.txt", "same");
        incoming.Write("mod.txt", "new");
        incoming.Write("new/added.txt", "hi");
        incoming.Write(".hidden", "dot file");
        incoming.Write(".git/config", "ignored");
        incoming.Write("bin/Debug/app.dll", "ignored");
        incoming.Write("src/obj/cache", "ignored");

        var builder = new DefaultFileManifestBuilder(new SnapshotFilter(new[] { "bin", "obj" }));
        var changes = await builder.BuildAsync(baseDir.Path, incoming.Path);

        changes.Select(change => (change.Kind, change.Path)).Should().Equal(
            (ChangeKind.Added, ".hidden"),
            (ChangeKind.Deleted, "gone/del.txt"),
            (ChangeKind.Modified, "mod.txt"),
            (ChangeKind.Added, "new/added.txt"));
        changes.Single(change => change.Path == "mod.txt").SizeBytes.Should().Be(3);
        changes.Single(change => change.Kind == ChangeKind.Deleted).Sha256.Should().BeNull();

        (await builder.BuildAsync(incoming.Path, incoming.Path)).Should().BeEmpty();
        (await builder.ScanAsync(Path.Combine(baseDir.Path, "missing"))).Should().BeEmpty();
    }
}
