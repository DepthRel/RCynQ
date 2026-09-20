using System.Text.Json;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Entities;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.State;

namespace RCynQ.Core.Data.Mapping;

/// <summary>
/// Maps between EF Core entities and Core domain records.
/// </summary>
internal static class EntityMapper
{
    /// <summary>
    /// JSON options used for JSON columns.
    /// </summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Maps a node entity to its domain record.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The node configuration.</returns>
    internal static NodeConfig ToDomain(NodeEntity entity)
        => new(new NodeId(entity.Id), entity.Name, (NodeRole)entity.Role, entity.RepoPath, entity.WorkdirPath);

    /// <summary>
    /// Copies a node configuration into an entity, preserving state columns.
    /// </summary>
    /// <param name="node">The node configuration.</param>
    /// <param name="entity">The target entity.</param>
    /// <param name="now">The update time.</param>
    internal static void Apply(NodeConfig node, NodeEntity entity, DateTimeOffset now)
    {
        entity.Id = node.Id.Value;
        entity.Name = node.Name;
        entity.Role = (int)node.Role;
        entity.RepoPath = node.RepoPath;
        entity.WorkdirPath = node.WorkdirPath;
        entity.UpdatedAtUtc = now;
    }

    /// <summary>
    /// Maps a peer entity to its domain record.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The peer.</returns>
    internal static PeerDescriptor ToDomain(PeerEntity entity)
        => new(
            new NodeId(entity.Id),
            entity.Name,
            (NodeRole)entity.Role,
            entity.Host,
            entity.SshPort,
            entity.SshUser,
            entity.HostKeyFingerprint,
            entity.SigningPublicKey,
            entity.GitRepoPath,
            entity.SshPublicKey,
            entity.EndpointMode,
            entity.TlsFingerprint,
            entity.EndpointPort);

    /// <summary>
    /// Copies a peer into an entity.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="entity">The target entity.</param>
    /// <param name="now">The update time.</param>
    internal static void Apply(PeerDescriptor peer, PeerEntity entity, DateTimeOffset now)
    {
        entity.Id = peer.Id.Value;
        entity.Name = peer.Name;
        entity.Role = (int)peer.Role;
        entity.Host = peer.Host;
        entity.SshPort = peer.SshPort;
        entity.SshUser = peer.SshUser;
        entity.HostKeyFingerprint = peer.HostKeyFingerprint;
        entity.SigningPublicKey = peer.SigningPublicKey;
        entity.GitRepoPath = peer.GitRepoPath;
        entity.SshPublicKey = peer.SshPublicKey;
        entity.EndpointMode = peer.EndpointMode;
        entity.TlsFingerprint = peer.TlsFingerprint;
        entity.EndpointPort = peer.EndpointPort;
        entity.UpdatedAtUtc = now;
    }

    /// <summary>
    /// Maps a chain link entity with a loaded peer to its domain record.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The link configuration.</returns>
    /// <exception cref="InvalidOperationException">The peer navigation is not loaded.</exception>
    internal static ChainLinkConfig ToDomain(ChainLinkEntity entity)
    {
        var peer = entity.Peer ?? throw new InvalidOperationException($"Peer of chain link {entity.Id} is not loaded.");
        return new ChainLinkConfig(
            entity.Id,
            entity.Name,
            (TransportKind)entity.Transport,
            ToDomain(peer),
            entity.LocalPort,
            entity.RemotePort,
            entity.Priority,
            JsonSerializer.Deserialize<List<TransportKind>>(entity.FallbackTransportsJson, Json),
            JsonSerializer.Deserialize<Dictionary<string, string>>(entity.TransportSettingsJson, Json));
    }

    /// <summary>
    /// Copies a chain link into an entity.
    /// </summary>
    /// <param name="link">The link configuration.</param>
    /// <param name="entity">The target entity.</param>
    internal static void Apply(ChainLinkConfig link, ChainLinkEntity entity)
    {
        entity.Id = link.Id;
        entity.Name = link.Name;
        entity.Transport = (int)link.Transport;
        entity.FallbackTransportsJson = JsonSerializer.Serialize(link.FallbackTransports, Json);
        entity.TransportSettingsJson = JsonSerializer.Serialize(link.TransportSettings, Json);
        entity.PeerId = link.Peer.Id.Value;
        entity.LocalPort = link.LocalPort;
        entity.RemotePort = link.RemotePort;
        entity.Priority = link.Priority;
    }

    /// <summary>
    /// Maps a patch entity to its domain record.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The metadata.</returns>
    internal static PatchMetadata ToDomain(PatchEnvelopeEntity entity)
        => new(
            entity.Id,
            (PatchDirection)entity.Direction,
            new NodeId(entity.SourceNode),
            new NodeId(entity.TargetNode),
            entity.CreatedAtUtc,
            entity.BaseStateHash,
            entity.IncomingStateHash,
            (PatchStatus)entity.Status,
            entity.AppliedAtUtc,
            JsonSerializer.Deserialize<List<FileChange>>(entity.ManifestJson, Json) ?? new List<FileChange>(),
            JsonSerializer.Deserialize<List<ConflictFile>>(entity.ConflictsJson, Json),
            entity.Error,
            entity.Signature);

    /// <summary>
    /// Copies patch metadata into an entity.
    /// </summary>
    /// <param name="patch">The metadata.</param>
    /// <param name="entity">The target entity.</param>
    internal static void Apply(PatchMetadata patch, PatchEnvelopeEntity entity)
    {
        entity.Id = patch.Id;
        entity.Direction = (int)patch.Direction;
        entity.SourceNode = patch.SourceNode.Value;
        entity.TargetNode = patch.TargetNode.Value;
        entity.CreatedAtUtc = patch.CreatedAtUtc;
        entity.BaseStateHash = patch.BaseStateHash;
        entity.IncomingStateHash = patch.IncomingStateHash;
        entity.Status = (int)patch.Status;
        entity.AppliedAtUtc = patch.AppliedAtUtc;
        entity.ManifestJson = JsonSerializer.Serialize(patch.Manifest, Json);
        entity.ConflictsJson = JsonSerializer.Serialize(patch.Conflicts, Json);
        entity.Error = patch.Error;
        entity.Signature = patch.Signature.Length == 0 ? null : patch.Signature;
    }

    /// <summary>
    /// Maps a snapshot entity to its domain record.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The snapshot.</returns>
    internal static SnapshotInfo ToDomain(SnapshotEntity entity)
        => new(entity.Id, new NodeId(entity.NodeId), entity.CreatedAtUtc, entity.StateHash, entity.Label);

    /// <summary>
    /// Maps a history entity to its domain record.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The history entry.</returns>
    internal static SyncHistoryEntry ToDomain(SyncHistoryEntity entity)
        => new(
            entity.Id,
            entity.TimestampUtc,
            (PatchDirection)entity.Direction,
            new NodeId(entity.SourceNode),
            new NodeId(entity.TargetNode),
            entity.PatchId,
            (PatchStatus)entity.Status,
            entity.AppliedFileCount,
            entity.ConflictCount,
            entity.Details);
}
