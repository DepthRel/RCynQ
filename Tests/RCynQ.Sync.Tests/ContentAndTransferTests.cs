using System.Text;
using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Sync.Content;
using RCynQ.Sync.Tests.Fixtures;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests for the gzip helpers, the content readers, the streamed applier and the update of the sync base after a delivery.
/// </summary>
public sealed class ContentAndTransferTests : IDisposable
{
    /// <summary>The fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>
    /// Verifies the gzip round trip.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Gzip_Round_Trips()
    {
        var text = string.Concat(Enumerable.Repeat("compress me\n", 10_000));

        var compressed = GzipCodec.Compress(Encoding.UTF8.GetBytes(text));
        var restored = await GzipCodec.DecompressAsync(new MemoryStream(compressed), maxBytes: 10_000_000);

        compressed.Length.Should().BeLessThan(text.Length / 10);
        Encoding.UTF8.GetString(restored).Should().Be(text);
    }

    /// <summary>
    /// Verifies that a small body that expands beyond the limit, and data that is not gzip, are refused.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Gzip_Decompression_Is_Bounded_And_Validated()
    {
        var bomb = GzipCodec.Compress(new byte[5_000_000]);

        bomb.Length.Should().BeLessThan(100_000, "the attack is a small request that expands to five million bytes");
        var tooLarge = () => GzipCodec.DecompressAsync(new MemoryStream(bomb), maxBytes: 1_000_000);
        var corrupt = () => GzipCodec.DecompressAsync(new MemoryStream("not gzip at all"u8.ToArray()), maxBytes: 1_000_000);

        await tooLarge.Should().ThrowAsync<InvalidDataException>();
        await corrupt.Should().ThrowAsync<InvalidDataException>();
    }

    /// <summary>
    /// Verifies that the streaming content compresses on the fly, announces gzip and disposes its source.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Gzip_Stream_Content_Compresses_Announces_And_Disposes_Source()
    {
        var source = new TrackedStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("streamed\n", 20_000))));
        var content = new GzipStreamContent(source);

        content.Headers.ContentEncoding.Should().Equal("gzip");
        content.Headers.ContentLength.Should().BeNull("the length of a compressed stream is not known in advance");
        var body = await content.ReadAsByteArrayAsync();
        content.Dispose();

        (await GzipCodec.DecompressAsync(new MemoryStream(body), 10_000_000)).Length.Should().Be(9 * 20_000);
        source.Disposed.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the working tree content is streamed, that a file open in another program can be read, and that missing files are reported.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workdir_Content_Streams_Files_And_Reports_Missing_Ones()
    {
        var content = _fixture.SourceContent;
        var a = new FileChange(ChangeKind.Modified, "a.txt", null, null, false);

        await using (var editor = new FileStream(Path.Combine(_fixture.SourceWork, "a.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            await using var stream = await content.OpenReadAsync(a);
            using var reader = new StreamReader(stream);
            (await reader.ReadToEndAsync()).Should().Be("v1\n");
        }

        var missing = () => content.OpenReadAsync(new FileChange(ChangeKind.Added, "nope.txt", null, null, false));
        var deleted = () => content.OpenReadAsync(new FileChange(ChangeKind.Deleted, "a.txt", null, null, false));
        (await missing.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("nope.txt");
        await deleted.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Verifies that the applier writes streamed content, replaces files, deletes files with their empty folders and refuses paths outside the target.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Applier_Writes_Deletes_And_Refuses_Unsafe_Paths()
    {
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");
        SyncFixture.Write(_fixture.SourceWork, "deep/er/n.txt", "new\n");
        File.Delete(Path.Combine(_fixture.SourceWork, "b.txt"));
        SyncFixture.Write(_fixture.DestWork, "gone/x/old.txt", "old\n");
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var manifest = envelope.Manifest.Append(new FileChange(ChangeKind.Deleted, "gone/x/old.txt", null, null, false)).ToArray();
        var withDeletion = new PatchEnvelope(envelope.Id, envelope.SourceNode, envelope.TargetNode, envelope.CreatedAtUtc, envelope.BaseStateHash, envelope.IncomingStateHash, manifest);

        var result = await _fixture.Applier.ApplyAsync(withDeletion, _fixture.SourceContent, _fixture.DestWork);

        result.AppliedFileCount.Should().Be(4);
        SyncFixture.Read(_fixture.DestWork, "a.txt").Should().Be("v2\n");
        SyncFixture.Read(_fixture.DestWork, "deep/er/n.txt").Should().Be("new\n");
        SyncFixture.Read(_fixture.DestWork, "b.txt").Should().BeNull();
        Directory.Exists(Path.Combine(_fixture.DestWork, "gone")).Should().BeFalse("empty parent folders of a deleted file are removed");
        Directory.GetFiles(_fixture.DestWork, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty("temporary files are removed");

        foreach (var unsafePath in new[] { "../escape.txt", ".git/hooks/post-commit", "sub/../../escape.txt" })
        {
            var hash = new string('a', 64);
            var hostile = new PatchEnvelope(envelope.Id, envelope.SourceNode, envelope.TargetNode, envelope.CreatedAtUtc, hash, hash,
                new[] { new FileChange(ChangeKind.Added, unsafePath, hash, 1, false) });
            var apply = () => _fixture.Applier.ApplyAsync(hostile, _fixture.SourceContent, _fixture.DestWork);
            await apply.Should().ThrowAsync<ArgumentException>(unsafePath);
        }
    }

    /// <summary>
    /// Verifies that the base takes over what was delivered but not what the user changed while the patch was on its way.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Base_Update_Skips_Files_Edited_After_The_Manifest_Was_Built()
    {
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");
        SyncFixture.Write(_fixture.SourceWork, "new.txt", "new\n");
        File.Delete(Path.Combine(_fixture.SourceWork, "b.txt"));
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v3, typed while sending\n");
        var syncBase = _fixture.SourceBaseForBuild;

        var skipped = await new Apply.SyncBaseUpdater(_fixture.Applier, _fixture.Paths).UpdateAsync(envelope, _fixture.SourceWork, syncBase);

        skipped.Should().Be(1);
        SyncFixture.Read(syncBase, "a.txt").Should().Be("v1\n", "the peer received v2, not v3, so the base must not claim v3");
        SyncFixture.Read(syncBase, "new.txt").Should().Be("new\n");
        SyncFixture.Read(syncBase, "b.txt").Should().BeNull("deletions are applied");

        var next = await _fixture.CreateBuilder().BuildAsync(envelope.SourceNode, envelope.TargetNode, syncBase, _fixture.SourceWork);
        next.Manifest.Should().ContainSingle().Which.Path.Should().Be("a.txt", "the next patch carries the edit");
    }

    /// <summary>
    /// Deletes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// A memory stream that remembers whether it was disposed.
    /// </summary>
    private sealed class TrackedStream : MemoryStream
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TrackedStream"/> class.
        /// </summary>
        /// <param name="bytes">The content.</param>
        public TrackedStream(byte[] bytes)
            : base(bytes)
        {
        }

        /// <summary>Gets a value indicating whether the stream was disposed.</summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
