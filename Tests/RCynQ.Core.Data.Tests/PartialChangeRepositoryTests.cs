using FluentAssertions;
using RCynQ.Core.Data.Repositories;
using RCynQ.Core.Data.Tests.Support;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Data.Tests;

/// <summary>
/// Integration tests of <see cref="PartialChangeRepository"/> against a migrated SQLite database.
/// </summary>
public sealed class PartialChangeRepositoryTests : IDisposable
{
    /// <summary>The time the choices are made at.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The test database.</summary>
    private readonly TestDatabase _database = new();

    /// <summary>
    /// Verifies that a choice is stored as it is, replaced by the next one of the same file and kept apart per repository.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Choice_Is_Stored_Replaced_And_Removed()
    {
        var repository = new PartialChangeRepository(_database.ContextFactory);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await repository.SaveAsync(new PartialChange(first, "src/a.cs", "base1", "work1", new byte[] { 1, 2 }, new[] { 0, 2 }, _now));
        await repository.SaveAsync(new PartialChange(first, "src/a.cs", null, "work2", new byte[] { 3 }, null, _now.AddMinutes(1)));
        await repository.SaveAsync(new PartialChange(second, "src/a.cs", "base3", "work3", new byte[] { 4 }, Array.Empty<int>(), _now));

        var stored = await repository.GetAsync(first, "src/a.cs");
        stored!.BaseSha256.Should().BeNull();
        stored.WorkdirSha256.Should().Be("work2");
        stored.Content.Should().Equal(3);
        stored.Blocks.Should().BeNull();
        stored.ChosenAtUtc.Should().Be(_now.AddMinutes(1));
        (await repository.GetAllAsync(first)).Should().ContainSingle();
        (await repository.GetAsync(second, "src/a.cs"))!.Blocks.Should().BeEmpty();

        (await repository.RemoveAsync(first, "src/a.cs")).Should().BeTrue();
        (await repository.RemoveAsync(first, "src/a.cs")).Should().BeFalse();
        (await repository.GetAllAsync(first)).Should().BeEmpty();
        (await repository.GetAllAsync(second)).Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that the indexes of the chosen changes come back in their order.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Chosen_Changes_Come_Back()
    {
        var repository = new PartialChangeRepository(_database.ContextFactory);
        var id = Guid.NewGuid();

        await repository.SaveAsync(new PartialChange(id, "b.txt", "b", "w", Array.Empty<byte>(), new[] { 1, 5, 10 }, _now));

        (await repository.GetAsync(id, "b.txt"))!.Blocks.Should().Equal(1, 5, 10);
    }

    /// <inheritdoc />
    public void Dispose() => _database.Dispose();
}
