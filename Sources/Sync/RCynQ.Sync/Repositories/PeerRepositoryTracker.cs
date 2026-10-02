using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Config;
using RCynQ.Core.Events;

namespace RCynQ.Sync.Repositories;

/// <summary>
/// Remembers which repository the node at the other end works with and tells the user interface when that, or the
/// repository of this node, changes.
/// </summary>
/// <remarks>
/// <para>
/// Each node names its repository whenever it pings the other one, and in its answer to a ping. So a node learns of a
/// switch at the next ping, whether or not it was running when the switch was made.
/// </para>
/// <para>
/// A node never follows the other one by itself: the user is told and decides. The one exception is not a switch at
/// all: a Workstation whose folder has no name yet takes the name the Origin uses (see
/// <see cref="RepositoryCatalog.BindAsync"/>).
/// </para>
/// </remarks>
public sealed class PeerRepositoryTracker
{
    /// <summary>The repositories of this node.</summary>
    private readonly RepositoryCatalog _catalog;

    /// <summary>The event publisher.</summary>
    private readonly IEventPublisher _events;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The name of the repository the other node works with, or <see langword="null"/> when not known.</summary>
    private volatile string? _peerRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerRepositoryTracker"/> class.
    /// </summary>
    /// <param name="catalog">The repositories of this node.</param>
    /// <param name="events">The event publisher.</param>
    /// <param name="timeProvider">The clock.</param>
    public PeerRepositoryTracker(RepositoryCatalog catalog, IEventPublisher events, TimeProvider timeProvider)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _catalog.Changed += (_, _) => _ = PublishAsync();
    }

    /// <summary>
    /// Gets the name of the repository the node at the other end works with, or <see langword="null"/> when not known.
    /// </summary>
    public string? PeerRepository => _peerRepository;

    /// <summary>
    /// Gets a value indicating whether both nodes are known to work with different repositories.
    /// </summary>
    public bool IsMismatch
        => _peerRepository is { } peer && _catalog.Current is { IsBound: true } current && !current.IsNamed(peer);

    /// <summary>
    /// Records what the other node said about its repository.
    /// </summary>
    /// <param name="name">The name it gave; <see langword="null"/>, blank or not a valid name changes nothing.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the name is recorded.</returns>
    public async Task RecordAsync(string? name, CancellationToken cancellationToken = default)
    {
        name = name?.Trim();
        if (!RepositoryName.IsValid(name))
        {
            return;
        }

        var changed = !string.Equals(_peerRepository, name, StringComparison.OrdinalIgnoreCase);
        _peerRepository = name;

        // Publishes by itself when it binds, through the change of the catalog.
        var bound = await _catalog.BindAsync(name, cancellationToken);
        if (changed && bound is null)
        {
            await PublishAsync();
        }
    }

    /// <summary>
    /// Publishes the current state.
    /// </summary>
    /// <returns>A task that completes when the event is published.</returns>
    private Task PublishAsync()
        => _events.PublishRepositoryAsync(new RepositoryEvent(
            _catalog.Current is { IsBound: true } current ? current.Name : null,
            _peerRepository,
            _timeProvider.GetUtcNow()));
}
