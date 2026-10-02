using FluentAssertions;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.State;
using RCynQ.Git.Hashing;
using RCynQ.Git.Manifest;
using RCynQ.Sync.State;
using RCynQ.Sync.Tests.Fixtures;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of how <see cref="StateTracker"/> keeps the stored state consistent.
/// </summary>
public sealed class StateTrackerTests : IDisposable
{
    /// <summary>The fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The state store.</summary>
    private readonly MemoryStateStore _store = new();

    /// <summary>
    /// Verifies that a refresh that was waiting for the working tree does not write back the time stamps it would have read
    /// before the wait: a patch sent meanwhile keeps its "last sent" time.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Refresh_Does_Not_Undo_A_Time_Stamp_Written_While_It_Waited()
    {
        var tracker = CreateTracker();
        var sentAt = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        Task<SyncSnapshot> refresh;

        // The working tree is busy (a patch is being sent), so the refresh waits.
        using (await _fixture.DestLock.AcquireAsync())
        {
            refresh = tracker.CaptureAsync();
            await Task.Delay(100);
            refresh.IsCompleted.Should().BeFalse("the refresh waits for the working tree");
            await _store.SaveAsync(new SyncState(null, null, sentAt, null));
        }

        var snapshot = await refresh;

        snapshot.State.LastSentAtUtc.Should().Be(sentAt);
        (await _store.GetAsync()).LastSentAtUtc.Should().Be(sentAt, "the refresh only renews the hashes");
        (await _store.GetAsync()).LocalStateHash.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that marking a patch as sent and as received at the same time keeps both time stamps.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Concurrent_Marks_Keep_Both_Time_Stamps()
    {
        var tracker = CreateTracker();

        await Task.WhenAll(
            Enumerable.Range(0, 8).Select(i => i % 2 == 0 ? tracker.MarkSentAsync() : tracker.MarkReceivedAsync()));

        var state = await _store.GetAsync();
        state.LastSentAtUtc.Should().NotBeNull();
        state.LastReceivedAtUtc.Should().NotBeNull();
    }

    /// <summary>
    /// Deletes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Creates the tracker of the Dest node on the in-memory store.
    /// </summary>
    /// <returns>The tracker.</returns>
    private StateTracker CreateTracker()
        => new(
            _store,
            new DefaultFileManifestBuilder(new SnapshotFilter()),
            new DefaultStateHasher(),
            _fixture.DestPaths,
            _fixture.DestNode,
            TimeProvider.System,
            _fixture.DestSyncBase,
            _fixture.DestLock);

    /// <summary>
    /// A state store in memory whose reads and writes take a moment, as those of a database do.
    /// </summary>
    private sealed class MemoryStateStore : INodeStateStore
    {
        /// <summary>The stored state.</summary>
        private SyncState _state = new(null, null, null, null);

        /// <inheritdoc />
        public async Task<SyncState> GetAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return Volatile.Read(ref _state);
        }

        /// <inheritdoc />
        public async Task SaveAsync(SyncState state, CancellationToken cancellationToken = default)
        {
            await Task.Delay(5, cancellationToken);
            Volatile.Write(ref _state, state);
        }
    }
}
