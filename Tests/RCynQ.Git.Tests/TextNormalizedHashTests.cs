using System.Text;
using FluentAssertions;
using RCynQ.Git.Hashing;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Tests for <see cref="DefaultStateHasher.Sha256TextNormalizedFileAsync"/>.
/// </summary>
public sealed class TextNormalizedHashTests : IDisposable
{
    /// <summary>The directory of the test files.</summary>
    private readonly TempDirectory _directory = new();

    /// <summary>
    /// Verifies that CRLF and LF versions of a text file have the same digest, equal to the plain digest of the LF version.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Crlf_And_Lf_Hash_The_Same()
    {
        var lf = Write("lf.txt", "a\nb\n");
        var crlf = Write("crlf.txt", "a\r\nb\r\n");

        var digest = await DefaultStateHasher.Sha256TextNormalizedFileAsync(crlf);

        digest.Should().Be(await DefaultStateHasher.Sha256TextNormalizedFileAsync(lf));
        digest.Should().Be(await DefaultStateHasher.Sha256FileAsync(lf));
    }

    /// <summary>
    /// Verifies that a lone CR is content, not a line ending, and that different text still hashes differently.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Lone_Cr_And_Real_Changes_Are_Kept()
    {
        var loneCr = Write("cr.txt", "a\rb\r");
        var other = Write("other.txt", "a\nc\n");

        (await DefaultStateHasher.Sha256TextNormalizedFileAsync(loneCr)).Should().Be(await DefaultStateHasher.Sha256FileAsync(loneCr));
        (await DefaultStateHasher.Sha256TextNormalizedFileAsync(other)).Should().NotBe(await DefaultStateHasher.Sha256TextNormalizedFileAsync(Write("lf.txt", "a\nb\n")));
    }

    /// <summary>
    /// Verifies that a CRLF split across read chunks is still recognized.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Crlf_Across_Chunk_Boundary_Is_Normalized()
    {
        var head = new string('x', 81919);
        var crlf = Write("big-crlf.txt", head + "\r\nend\r\n");
        var lf = Write("big-lf.txt", head + "\nend\n");

        (await DefaultStateHasher.Sha256TextNormalizedFileAsync(crlf)).Should().Be(await DefaultStateHasher.Sha256FileAsync(lf));
    }

    /// <summary>
    /// Verifies that a binary file (a NUL byte near the start) is hashed byte for byte.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Binary_File_Is_Hashed_As_It_Is()
    {
        var path = _directory["bin.dat"];
        await File.WriteAllBytesAsync(path, new byte[] { 0, 13, 10, 65 });

        (await DefaultStateHasher.Sha256TextNormalizedFileAsync(path)).Should().Be(await DefaultStateHasher.Sha256FileAsync(path));
    }

    /// <inheritdoc />
    public void Dispose() => _directory.Dispose();

    /// <summary>
    /// Writes a file without any conversion.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="content">The content.</param>
    /// <returns>The full path.</returns>
    private string Write(string name, string content)
    {
        var path = _directory[name];
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }
}
