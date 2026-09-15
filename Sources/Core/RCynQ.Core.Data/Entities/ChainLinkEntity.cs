namespace RCynQ.Core.Data.Entities;

/// <summary>
/// EF Core entity for a chain link (table <c>ChainLinks</c>).
/// </summary>
public sealed class ChainLinkEntity
{
    /// <summary>Gets or sets the link identifier (primary key).</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the unique display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the primary transport as its integer value.</summary>
    public int Transport { get; set; }

    /// <summary>Gets or sets the JSON array of fallback transport names.</summary>
    public string FallbackTransportsJson { get; set; } = "[]";

    /// <summary>Gets or sets the JSON object with transport settings.</summary>
    public string TransportSettingsJson { get; set; } = "{}";

    /// <summary>Gets or sets the peer identifier (foreign key to <c>Peers</c>).</summary>
    public string PeerId { get; set; } = string.Empty;

    /// <summary>Gets or sets the peer navigation property.</summary>
    public PeerEntity? Peer { get; set; }

    /// <summary>Gets or sets the local forward port.</summary>
    public int LocalPort { get; set; }

    /// <summary>Gets or sets the remote port.</summary>
    public int RemotePort { get; set; }

    /// <summary>Gets or sets the chain position.</summary>
    public int Priority { get; set; }
}
