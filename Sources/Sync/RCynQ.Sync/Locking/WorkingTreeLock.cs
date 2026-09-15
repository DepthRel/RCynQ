using System.Text;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.State;

namespace RCynQ.Sync.Locking;

/// <summary>
/// Serializes changes to the working tree and the sync base.
/// </summary>
public sealed class WorkingTreeLock
{
    /// <summary>
    /// The semaphore.
    /// </summary>
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Acquires the lock.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel waiting.</param>
    /// <returns>A handle that releases the lock when disposed.</returns>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    /// <summary>
    /// Releases the semaphore once.
    /// </summary>
    private sealed class Releaser : IDisposable
    {
        /// <summary>
        /// The semaphore, cleared after release.
        /// </summary>
        private SemaphoreSlim? _semaphore;

        /// <summary>
        /// Initializes a new instance of the <see cref="Releaser"/> class.
        /// </summary>
        /// <param name="semaphore">The semaphore.</param>
        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        /// <inheritdoc />
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
