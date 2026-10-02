namespace RCynQ.Sync.Locking;

/// <summary>
/// Serializes operations that read or modify the working tree and the sync base of a node.
/// </summary>
/// <remarks>
/// <para>
/// Inside one process the lock is a semaphore. The service and the CLI are two processes on the same working tree, the
/// same sync base and the same data directory, so a lock created with a lock file also holds that file open for exclusive
/// use while it is taken: the other process waits until it is released.
/// </para>
/// <para>
/// The file is only a token: nothing is written to it. The operating system releases it when the process that holds it
/// ends, however it ends, so a crash never leaves the node locked.
/// </para>
/// </remarks>
public sealed class WorkingTreeLock
{
    /// <summary>
    /// The name of the lock file in the data directory of a node.
    /// </summary>
    public const string LOCK_FILE_NAME = "working-tree.lock";

    /// <summary>
    /// How long to wait before asking again for a lock file another process holds.
    /// </summary>
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The semaphore that serializes callers of this process.
    /// </summary>
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// The lock file shared with other processes, or <see langword="null"/> for a lock of this process only.
    /// </summary>
    private readonly string? _lockFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkingTreeLock"/> class.
    /// </summary>
    /// <param name="lockFile">
    /// The lock file shared by every process that works on the node, or <see langword="null"/> for a lock that only
    /// serializes the callers of this process.
    /// </param>
    public WorkingTreeLock(string? lockFile = null)
    {
        _lockFile = string.IsNullOrWhiteSpace(lockFile) ? null : Path.GetFullPath(lockFile);
    }

    /// <summary>
    /// Waits until the lock is available and acquires it.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A handle that releases the lock when disposed.</returns>
    /// <exception cref="UnauthorizedAccessException">The lock file cannot be created or opened.</exception>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return new Releaser(_semaphore, await OpenLockFileAsync(cancellationToken));
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// Opens the lock file for exclusive use, waiting while another process holds it.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The open file, or <see langword="null"/> when the lock has no file.</returns>
    private async Task<FileStream?> OpenLockFileAsync(CancellationToken cancellationToken)
    {
        if (_lockFile is null)
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_lockFile)!);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(_lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.None);
            }
            catch (IOException)
            {
                // Held by another process (a sharing violation is the only I/O failure of opening an existing file of
                // our own directory). It is asked again shortly; the wait ends when the caller cancels.
                await Task.Delay(_retryDelay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Releases the lock on dispose.
    /// </summary>
    private sealed class Releaser : IDisposable
    {
        /// <summary>
        /// The semaphore to release; <see langword="null"/> after disposal.
        /// </summary>
        private SemaphoreSlim? _semaphore;

        /// <summary>
        /// The open lock file, or <see langword="null"/>.
        /// </summary>
        private readonly FileStream? _file;

        /// <summary>
        /// Initializes a new instance of the <see cref="Releaser"/> class.
        /// </summary>
        /// <param name="semaphore">The semaphore to release.</param>
        /// <param name="file">The open lock file, or <see langword="null"/>.</param>
        public Releaser(SemaphoreSlim semaphore, FileStream? file)
        {
            _semaphore = semaphore;
            _file = file;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _semaphore, null) is not { } semaphore)
            {
                return;
            }

            // The file first: another process may take it at once, and callers of this process queue on the semaphore.
            _file?.Dispose();
            semaphore.Release();
        }
    }
}
