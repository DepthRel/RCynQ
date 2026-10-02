using FluentAssertions;
using RCynQ.Git.Hashing;
using RCynQ.Git.Manifest;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Tests of <see cref="FileHashCache"/>: a digest is reused while the size and the modification time of the file stay,
/// and a file that was written a moment ago is never remembered.
/// </summary>
public sealed class FileHashCacheTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly TempDirectory _temp = new();

    /// <summary>
    /// Verifies that a file that has rested is remembered and that a later write, which moves its modification time,
    /// is seen.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Rested_File_Is_Remembered_Until_It_Is_Written_Again()
    {
        var cache = new FileHashCache(TimeProvider.System);
        var path = _temp.Write("a.txt", "one");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));

        var first = await cache.GetSha256Async(new FileInfo(path));

        first.Should().Be(DefaultStateHasher.Sha256Hex("one"u8));
        cache.Count.Should().Be(1);
        (await cache.GetSha256Async(new FileInfo(path))).Should().Be(first);

        File.WriteAllText(path, "two");
        (await cache.GetSha256Async(new FileInfo(path))).Should().Be(DefaultStateHasher.Sha256Hex("two"u8), "the write moved the modification time");
    }

    /// <summary>
    /// Verifies that a file written a moment ago is read every time: another write within the resolution of the file
    /// system clock would leave the same size and time on other content.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Freshly_Written_File_Is_Not_Remembered()
    {
        var cache = new FileHashCache(TimeProvider.System);
        var path = _temp.Write("a.txt", "one");
        var written = File.GetLastWriteTimeUtc(path);

        (await cache.GetSha256Async(new FileInfo(path))).Should().Be(DefaultStateHasher.Sha256Hex("one"u8));
        cache.Count.Should().Be(0);

        // Same size, same time, other content: exactly the case the rule exists for.
        File.WriteAllText(path, "two");
        File.SetLastWriteTimeUtc(path, written);
        (await cache.GetSha256Async(new FileInfo(path))).Should().Be(DefaultStateHasher.Sha256Hex("two"u8));
    }

    /// <summary>
    /// Verifies that a scan with a cache gives the same manifest as a scan without one, before and after an edit.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Scan_With_A_Cache_Equals_A_Scan_Without()
    {
        var old = DateTime.UtcNow.AddHours(-2);
        foreach (var (name, text) in new[] { ("a.txt", "alpha"), ("sub/b.txt", "beta"), ("sub/deep/c.bin", "gamma") })
        {
            File.SetLastWriteTimeUtc(_temp.Write(name, text), old);
        }

        var cached = new DefaultFileManifestBuilder(new SnapshotFilter(), new FileHashCache(TimeProvider.System));
        var plain = new DefaultFileManifestBuilder(new SnapshotFilter());

        (await cached.ScanAsync(_temp.Path)).Should().Equal(await plain.ScanAsync(_temp.Path));
        (await cached.ScanAsync(_temp.Path)).Should().Equal(await plain.ScanAsync(_temp.Path), "the second scan answers from the cache");

        _temp.Write("sub/b.txt", "beta, edited");
        File.Delete(Path.Combine(_temp.Path, "a.txt"));
        (await cached.ScanAsync(_temp.Path)).Should().Equal(await plain.ScanAsync(_temp.Path));
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose() => _temp.Dispose();
}
