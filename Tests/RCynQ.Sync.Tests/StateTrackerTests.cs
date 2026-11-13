using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Events;
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
        using (await _fixture.OriginLock.AcquireAsync())
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
    /// Verifies that the end of a look at the working tree is told to the clients, so a page that showed the preliminary
    /// summary of the start asks for the full one.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Capture_Tells_The_Clients_It_Is_Done()
    {
        var events = Substitute.For<IEventPublisher>();
        var tracker = CreateTracker(events);

        await tracker.CaptureAsync();

        await events.Received(1).PublishStateCapturedAsync(Arg.Any<StateCapturedEvent>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a requested capture looks at the working tree in the background and tells the clients when it is
    /// done, so a summary that did not wait for it is completed later.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Requested_Capture_Runs_In_The_Background()
    {
        var events = Substitute.For<IEventPublisher>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        events.PublishStateCapturedAsync(Arg.Any<StateCapturedEvent>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            done.TrySetResult();
            return Task.CompletedTask;
        });
        var tracker = CreateTracker(events);
        tracker.Latest.Should().BeNull();

        tracker.RequestCapture();

        (await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(done.Task, "the look is told when it is done");
        tracker.Latest.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that a capture requested while a look at the working tree runs (the look of the start, while the window
    /// asks for the summary) starts no second look.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Requested_Capture_Joins_The_Look_That_Runs()
    {
        var events = Substitute.For<IEventPublisher>();
        var tracker = CreateTracker(events);
        Task<SyncSnapshot> start;

        using (await _fixture.OriginLock.AcquireAsync())
        {
            start = tracker.CaptureAsync();
            tracker.RequestCapture();
            tracker.RequestCapture();
        }

        await start;
        await Task.Delay(300);

        await events.Received(1).PublishStateCapturedAsync(Arg.Any<StateCapturedEvent>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Deletes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Creates the tracker of the Origin node on the in-memory store.
    /// </summary>
    /// <param name="events">Tells the clients that a capture is done; <see langword="null"/> tells nobody.</param>
    /// <returns>The tracker.</returns>
    private StateTracker CreateTracker(IEventPublisher? events = null)
        => new(
            _store,
            new DefaultFileManifestBuilder(new SnapshotFilter()),
            new DefaultStateHasher(),
            _fixture.OriginPaths,
            _fixture.OriginNode,
            TimeProvider.System,
            _fixture.OriginSyncBase,
            _fixture.OriginLock,
            events);

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
