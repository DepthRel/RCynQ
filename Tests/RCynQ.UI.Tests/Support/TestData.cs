using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.State;

namespace RCynQ.UI.Tests.Support;

/// <summary>
/// Builders of service DTOs used by the UI tests.
/// </summary>
public static class TestData
{
    /// <summary>A valid state hash.</summary>
    public static readonly string HashA = new('a', 64);

    /// <summary>Another valid state hash.</summary>
    public static readonly string HashB = new('b', 64);

    /// <summary>
    /// Creates a link DTO.
    /// </summary>
    /// <param name="name">The link name.</param>
    /// <param name="peer">The peer node identifier.</param>
    /// <param name="role">The peer role.</param>
    /// <param name="host">The peer host.</param>
    /// <param name="transport">The transport.</param>
    /// <returns>The DTO.</returns>
    public static LinkDto Link(string name, string peer, NodeRole role, string host, TransportKind transport = TransportKind.Direct)
        => new(
            Guid.NewGuid(),
            name,
            transport,
            Array.Empty<TransportKind>(),
            new Dictionary<string, string>(),
            new NodeId(peer),
            peer,
            role,
            host,
            22,
            "rcynq",
            null,
            false,
            null,
            0,
            role == NodeRole.Mediator ? 22 : 18443,
            role == NodeRole.Mediator ? 10 : 1000);

    /// <summary>
    /// Creates a link state.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="status">The status.</param>
    /// <returns>The state.</returns>
    public static LinkState State(LinkDto link, LinkStatus status)
        => new(link.Id, link.Name, link.Transport, status, null, DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a node configuration.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="workdir">The working copy path.</param>
    /// <returns>The configuration.</returns>
    public static NodeConfigurationDto Config(NodeRole role, string? workdir = "/home/dev/projects/app")
        => new(
            new NodeId("node-1"),
            "Node",
            role,
            role == NodeRole.Dest ? workdir : null,
            role == NodeRole.Source ? workdir : null,
            18443,
            RoleCapabilities.For(role),
            null);

    /// <summary>
    /// Creates a synchronization status.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The status.</returns>
    public static NodeSyncStatus Status(NodeRole role = NodeRole.Source)
        => new(new NodeId("node-1"), "Node", role, new SyncState(HashA, HashA, null, null), true, Array.Empty<FileChange>(), 0);

    /// <summary>
    /// Creates metadata of an incoming patch waiting for a conflict decision.
    /// </summary>
    /// <returns>The metadata.</returns>
    public static PatchMetadata ConflictPatch()
        => new(
            Guid.NewGuid(),
            PatchDirection.Incoming,
            new NodeId("dest-1"),
            new NodeId("node-1"),
            DateTimeOffset.UtcNow,
            HashA,
            HashB,
            PatchStatus.ConflictPending,
            null,
            Array.Empty<FileChange>(),
            new[] { new ConflictFile("src/app.cs", HashA, HashB, "both sides changed") });
}
