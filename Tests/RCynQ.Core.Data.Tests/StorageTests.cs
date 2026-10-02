using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Data.Tests.Support;
using RCynQ.Core.Exceptions;

namespace RCynQ.Core.Data.Tests;

/// <summary>
/// Tests for the secret store and patch file storage.
/// </summary>
public sealed class StorageTests : IDisposable
{
    /// <summary>
    /// The test database.
    /// </summary>
    private readonly TestDatabase _database = new();

    /// <summary>
    /// Verifies that secrets are encrypted at rest and decrypt back to the original value.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SecretStore_Encrypts_And_Decrypts()
    {
        var store = new AesGcmSecretStore(TestTexts.English, _database.ContextFactory, _database.AppData, TimeProvider.System);

        await store.SetAsync("link-token", "super-secret-value");

        await using (var db = await _database.ContextFactory.CreateDbContextAsync())
        {
            var row = await db.Secrets.SingleAsync();
            System.Text.Encoding.UTF8.GetString(row.CipherText).Should().NotContain("super-secret");
            row.Nonce.Should().HaveCount(12);
            row.Tag.Should().HaveCount(16);
        }

        (await store.GetAsync("link-token")).Should().Be("super-secret-value");
        (await new AesGcmSecretStore(TestTexts.English, _database.ContextFactory, _database.AppData, TimeProvider.System).GetAsync("link-token"))
            .Should().Be("super-secret-value", "a new instance reads the same key file");
        (await store.ListAsync()).Should().Equal("link-token");
        (await store.GetAsync("missing")).Should().BeNull();
        (await store.DeleteAsync("link-token")).Should().BeTrue();
        (await store.GetAsync("link-token")).Should().BeNull();
    }

    /// <summary>
    /// Verifies that tampering with the ciphertext or moving it to another name is detected.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SecretStore_Detects_Tampering()
    {
        var store = new AesGcmSecretStore(TestTexts.English, _database.ContextFactory, _database.AppData, TimeProvider.System);
        await store.SetAsync("a", "value");

        await using (var db = await _database.ContextFactory.CreateDbContextAsync())
        {
            var row = await db.Secrets.SingleAsync();
            db.Secrets.Remove(row);
            await db.SaveChangesAsync();
            db.Secrets.Add(new Entities.SecretEntity { Name = "b", Nonce = row.Nonce, CipherText = row.CipherText, Tag = row.Tag });
            await db.SaveChangesAsync();
        }

        var read = () => store.GetAsync("b");
        await read.Should().ThrowAsync<SyncBridgeException>();
    }

    /// <summary>
    /// Verifies saving and reading the diff of a patch.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchFileStorage_Saves_And_Reads_The_Diff()
    {
        var storage = new PatchFileStorage(TestTexts.English, _database.AppData);
        var id = Guid.NewGuid();

        (await storage.ExistsAsync(id)).Should().BeFalse();
        var readMissing = () => storage.ReadDiffAsync(id);
        await readMissing.Should().ThrowAsync<SyncBridgeException>();

        await storage.SaveDiffAsync(id, new byte[] { 1, 2, 3 });

        (await storage.ExistsAsync(id)).Should().BeTrue();
        (await storage.ReadDiffAsync(id)).Should().Equal(1, 2, 3);
        Directory.GetFiles(storage.GetPatchDirectory(id)).Should().ContainSingle("temporary files are removed");
    }

    /// <summary>
    /// Verifies that verified uploads are staged for a patch, read back and deleted, and that the diff survives.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchFileStorage_Uploads_Are_Staged_Read_And_Deleted()
    {
        var storage = new PatchFileStorage(TestTexts.English, _database.AppData);
        var id = Guid.NewGuid();
        var content = "hello\n"u8.ToArray();
        var hash = Hash(content);

        (await storage.HasContentAsync(id, hash)).Should().BeFalse();
        await storage.SaveUploadAsync(hash, new MemoryStream(content), content.Length);
        (await storage.HasContentAsync(id, hash)).Should().BeTrue("the upload is available to any patch");

        await storage.SaveDiffAsync(id, new byte[] { 9 });
        await storage.StageContentAsync(id, new[] { hash });
        await using (var stream = await storage.OpenContentAsync(id, hash))
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            copy.ToArray().Should().Equal(content);
        }

        (await storage.HasContentAsync(Guid.NewGuid(), hash)).Should().BeFalse("staging moved the upload into the patch");

        await storage.DeleteContentAsync(id);

        (await storage.HasContentAsync(id, hash)).Should().BeFalse();
        (await storage.ReadDiffAsync(id)).Should().Equal(9);
    }

    /// <summary>
    /// Verifies that content with a wrong hash, or larger than announced, is refused and leaves nothing behind.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchFileStorage_Refuses_Wrong_Or_Oversized_Uploads()
    {
        var storage = new PatchFileStorage(TestTexts.English, _database.AppData);
        var content = "hello\n"u8.ToArray();
        var hash = Hash(content);

        var wrongHash = () => storage.SaveUploadAsync(hash, new MemoryStream("other\n"u8.ToArray()), 100);
        var tooLarge = () => storage.SaveUploadAsync(hash, new MemoryStream(content), content.Length - 1);

        await wrongHash.Should().ThrowAsync<FormatException>();
        await tooLarge.Should().ThrowAsync<FormatException>();
        (await storage.HasContentAsync(Guid.NewGuid(), hash)).Should().BeFalse();
        var uploads = Path.Combine(_database.AppData.Create().PatchesDirectory, PatchFileStorage.UPLOADS_DIRECTORY_NAME);
        Directory.GetFiles(uploads).Should().BeEmpty("partial files are removed");
    }

    /// <summary>
    /// Verifies that only well-formed hashes reach the file system, so a hash cannot name a path outside the storage.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("../../evil")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task PatchFileStorage_Refuses_Malformed_Hashes(string hash)
    {
        var storage = new PatchFileStorage(TestTexts.English, _database.AppData);

        var save = () => storage.SaveUploadAsync(hash, new MemoryStream(new byte[1]), 1);
        var open = () => storage.OpenContentAsync(Guid.NewGuid(), hash);

        await save.Should().ThrowAsync<ArgumentException>();
        await open.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Verifies that staging content that was never uploaded fails and that old uploads are pruned.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchFileStorage_Stage_Fails_For_Missing_Content_And_Prunes_Old_Uploads()
    {
        var storage = new PatchFileStorage(TestTexts.English, _database.AppData);
        var content = "old\n"u8.ToArray();
        var hash = Hash(content);
        var missing = () => storage.StageContentAsync(Guid.NewGuid(), new[] { hash });
        await missing.Should().ThrowAsync<SyncBridgeException>();

        await storage.SaveUploadAsync(hash, new MemoryStream(content), content.Length);
        await storage.PruneUploadsAsync(DateTimeOffset.UtcNow.AddHours(-1));
        (await storage.HasContentAsync(Guid.NewGuid(), hash)).Should().BeTrue("a fresh upload is kept");

        await storage.PruneUploadsAsync(DateTimeOffset.UtcNow.AddHours(1));
        (await storage.HasContentAsync(Guid.NewGuid(), hash)).Should().BeFalse("an upload older than the limit is deleted");
    }

    /// <summary>
    /// Computes the SHA-256 hash of a buffer as lowercase hex.
    /// </summary>
    /// <param name="content">The buffer.</param>
    /// <returns>The hash.</returns>
    private static string Hash(byte[] content) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>
    /// Deletes the test database.
    /// </summary>
    public void Dispose() => _database.Dispose();
}
