using RCynQ.Core.Abstractions.Storage;

namespace RCynQ.Sync.Merging;

/// <summary>
/// Keeps the merge sessions of the service: in memory, with their files in
/// <see cref="Core.Config.AppDataPaths.MergeSessionsDirectory"/>.
/// </summary>
/// <remarks>
/// A patch has one session at a time; a new one replaces the old. A session ends when it is applied or given up, and
/// after <see cref="Lifetime"/> at the latest. Sessions do not outlive the process: the folders a stopped service left
/// behind are removed before the next session is prepared.
/// </remarks>
public sealed class MergeSessionStore
{
    /// <summary>
    /// How long a session lives: enough for the longest merge, short enough that forgotten files do not pile up.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    /// <summary>The sessions by their identifier.</summary>
    private readonly Dictionary<Guid, MergeSession> _sessions = new();

    /// <summary>Serializes the access to the sessions.</summary>
    private readonly object _gate = new();

    /// <summary>The node data directories.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeSessionStore"/> class.
    /// </summary>
    /// <param name="appData">The node data directories.</param>
    /// <param name="timeProvider">The clock.</param>
    public MergeSessionStore(IAppDataFactory appData, TimeProvider timeProvider)
    {
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Creates the empty directory of a new session, after removing the sessions that expired and the folders that belong
    /// to no session.
    /// </summary>
    /// <param name="sessionId">The identifier of the new session.</param>
    /// <returns>The full path of the directory.</returns>
    public string CreateDirectory(Guid sessionId)
    {
        var root = _appData.Create().MergeSessionsDirectory;
        lock (_gate)
        {
            RemoveExpired();
            var known = _sessions.Values.Select(session => Path.GetFullPath(session.Directory)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(root))
            {
                foreach (var leftover in Directory.GetDirectories(root).Where(directory => !known.Contains(Path.GetFullPath(directory))))
                {
                    DeleteQuietly(leftover);
                }
            }
        }

        var directory = Path.Combine(root, sessionId.ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Keeps a prepared session, replacing the one of the same patch.
    /// </summary>
    /// <param name="session">The session.</param>
    public void Add(MergeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_gate)
        {
            foreach (var old in _sessions.Values.Where(old => old.PatchId == session.PatchId).ToList())
            {
                Drop(old);
            }

            _sessions[session.Id] = session;
        }
    }

    /// <summary>
    /// Finds a session that has not expired.
    /// </summary>
    /// <param name="sessionId">The identifier of the session.</param>
    /// <returns>The session, or <see langword="null"/> when it is unknown or expired.</returns>
    public MergeSession? Get(Guid sessionId)
    {
        lock (_gate)
        {
            RemoveExpired();
            return _sessions.GetValueOrDefault(sessionId);
        }
    }

    /// <summary>
    /// Ends a session and removes its files. Does nothing for an unknown session.
    /// </summary>
    /// <param name="sessionId">The identifier of the session.</param>
    /// <returns><see langword="true"/> when the session existed.</returns>
    public bool Remove(Guid sessionId)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return false;
            }

            Drop(session);
            return true;
        }
    }

    /// <summary>
    /// Ends the session of a patch that was decided some other way.
    /// </summary>
    /// <param name="patchId">The patch.</param>
    public void RemoveForPatch(Guid patchId)
    {
        lock (_gate)
        {
            foreach (var session in _sessions.Values.Where(session => session.PatchId == patchId).ToList())
            {
                Drop(session);
            }
        }
    }

    /// <summary>
    /// Ends the sessions older than <see cref="Lifetime"/>. Called under the lock.
    /// </summary>
    private void RemoveExpired()
    {
        var cutoff = _timeProvider.GetUtcNow() - Lifetime;
        foreach (var session in _sessions.Values.Where(session => session.CreatedAt < cutoff).ToList())
        {
            Drop(session);
        }
    }

    /// <summary>
    /// Forgets a session and removes its files. Called under the lock.
    /// </summary>
    /// <param name="session">The session.</param>
    private void Drop(MergeSession session)
    {
        _sessions.Remove(session.Id);
        DeleteQuietly(session.Directory);
    }

    /// <summary>
    /// Removes a directory with everything in it, leaving it when it is in use.
    /// </summary>
    /// <param name="directory">The directory.</param>
    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A merge tool may still hold a file; the folder is removed before the next session is prepared.
        }
    }
}
