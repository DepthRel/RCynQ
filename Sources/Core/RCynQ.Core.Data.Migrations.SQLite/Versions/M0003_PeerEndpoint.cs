using FluentMigrator;
using RCynQ.Core.Data.Migrations.Base;

namespace RCynQ.Core.Data.Migrations.SQLite.Versions;

/// <summary>
/// Migration 0003: stores how a peer accepts connections (<c>Scripts/0003_PeerEndpoint.Up.sql</c>) — through its SSH
/// server or on its own TLS port, with the pinned certificate fingerprint; removed with
/// <c>Scripts/0003_PeerEndpoint.Down.sql</c>.
/// </summary>
[Migration(3, "Peer endpoint mode")]
public sealed class M0003_PeerEndpoint : ScriptMigration
{
    /// <inheritdoc />
    protected override string ScriptName => "0003_PeerEndpoint";
}
