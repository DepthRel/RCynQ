using FluentAssertions;
using RCynQ.Sync.Locking;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="WorkingTreeLock"/>, within one process and between processes (two locks on one lock file).
/// </summary>
public sealed class WorkingTreeLockTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rcynq-lock-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies that a second caller of the same lock waits until the first one releases it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Callers_Of_One_Lock_Take_Turns()
    {
        var gate = new WorkingTreeLock();
        var first = await gate.AcquireAsync();

        var second = gate.AcquireAsync();
        await Task.Delay(150);
        second.IsCompleted.Should().BeFalse();

        first.Dispose();
        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    /// <summary>
    /// Verifies that two locks on the same lock file, as the service and the CLI of one node have, exclude each other,
    /// and that the waiting one gets the lock as soon as the other releases it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Locks_On_The_Same_File_Exclude_Each_Other()
    {
        var file = Path.Combine(_directory, "data", WorkingTreeLock.LOCK_FILE_NAME);
        var service = new WorkingTreeLock(file);
        var cli = new WorkingTreeLock(file);
        var held = await service.AcquireAsync();

        var waiting = cli.AcquireAsync();
        await Task.Delay(300);
        waiting.IsCompleted.Should().BeFalse("the other process holds the node");

        held.Dispose();
        var acquired = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        acquired.Dispose();
        (await service.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    /// <summary>
    /// Verifies that a wait for a lock another process holds ends when the caller cancels, and leaves the lock usable.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Waiting_For_Another_Process_Can_Be_Cancelled()
    {
        var file = Path.Combine(_directory, WorkingTreeLock.LOCK_FILE_NAME);
        var holder = new WorkingTreeLock(file);
        var waiter = new WorkingTreeLock(file);
        using var held = await holder.AcquireAsync();
        using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var wait = () => waiter.AcquireAsync(giveUp.Token);

        await wait.Should().ThrowAsync<OperationCanceledException>();
        held.Dispose();
        (await waiter.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }
}
