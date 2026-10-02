namespace RCynQ.Core.Events;

/// <summary>
/// Published when the repository the node works with changes, when its list of repositories changes, or when the node
/// learns that the node at the other end works with another repository.
/// </summary>
/// <param name="Repository">The name of the repository this node works with, or <see langword="null"/>.</param>
/// <param name="PeerRepository">The name of the repository the node at the other end works with, as far as known.</param>
/// <param name="TimestampUtc">When the event occurred.</param>
public sealed record RepositoryEvent(string? Repository, string? PeerRepository, DateTimeOffset TimestampUtc);
