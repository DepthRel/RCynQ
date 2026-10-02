namespace RCynQ.Core.Api;

/// <summary>
/// The result of allowing the imported neighbours to connect to this node through its SSH server.
/// </summary>
/// <param name="Applied">Whether the <c>authorized_keys</c> file was updated.</param>
/// <param name="RequiresElevation">Whether administrator rights are needed to write the file (Windows administrators file).</param>
/// <param name="FilePath">The <c>authorized_keys</c> file of the local SSH server.</param>
/// <param name="EntryCount">The number of neighbour keys that need access.</param>
/// <param name="PeersWithoutKey">Neighbours that need access but whose export contained no SSH public key.</param>
/// <param name="PeersWaitingForCounterpart">Neighbours of a Relay that cannot be allowed until the opposite endpoint is imported.</param>
/// <param name="Message">A summary for logs and the CLI.</param>
public sealed record PeerAccessResult(
    bool Applied,
    bool RequiresElevation,
    string FilePath,
    int EntryCount,
    IReadOnlyList<string> PeersWithoutKey,
    IReadOnlyList<string> PeersWaitingForCounterpart,
    string Message);
