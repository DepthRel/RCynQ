using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Data.Entities;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Data.Tests.Support;
using RCynQ.Core.Exceptions;
using Xunit;

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
        var store = new AesGcmSecretStore(_database.ContextFactory, _database.AppData, TimeProvider.System);

        await store.SetAsync("link-token", "super-secret-value");

        await using (var db = await _database.ContextFactory.CreateDbContextAsync())
        {
            var row = await db.Secrets.SingleAsync();
            System.Text.Encoding.UTF8.GetString(row.CipherText).Should().NotContain("super-secret");
            row.Nonce.Should().HaveCount(12);
            row.Tag.Should().HaveCount(16);
        }

        (await store.GetAsync("link-token")).Should().Be("super-secret-value");
        (await new AesGcmSecretStore(_database.ContextFactory, _database.AppData, TimeProvider.System).GetAsync("link-token"))
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
        var store = new AesGcmSecretStore(_database.ContextFactory, _database.AppData, TimeProvider.System);
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
    /// Verifies saving and reading patch payload files.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchFileStorage_Saves_And_Reads()
    {
        var storage = new PatchFileStorage(_database.AppData);
        var id = Guid.NewGuid();

        (await storage.ExistsAsync(id)).Should().BeFalse();
        var readMissing = () => storage.ReadDiffAsync(id);
        await readMissing.Should().ThrowAsync<SyncBridgeException>();

        await storage.SaveAsync(id, new byte[] { 1, 2, 3 }, new byte[] { 4, 5 });

        (await storage.ExistsAsync(id)).Should().BeTrue();
        (await storage.ReadDiffAsync(id)).Should().Equal(1, 2, 3);
        (await storage.ReadBundleAsync(id)).Should().Equal(4, 5);
        Directory.GetFiles(storage.GetPatchDirectory(id)).Should().HaveCount(2, "temporary files are removed");
    }

    /// <summary>
    /// Deletes the test database.
    /// </summary>
    public void Dispose() => _database.Dispose();
}
