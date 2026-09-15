using FluentMigrator;
using RCynQ.Core.Data.Migrations.Base;

namespace RCynQ.Core.Data.Migrations.SQLite.Versions;

/// <summary>
/// Migration 0002: stores the SSH public key of imported peers (<c>Scripts/0002_PeerSshPublicKey.Up.sql</c>), used to
/// allow their connections in <c>authorized_keys</c>; removed with <c>Scripts/0002_PeerSshPublicKey.Down.sql</c>.
/// </summary>
[Migration(2, "Peer SSH public key")]
public sealed class M0002_PeerSshPublicKey : ScriptMigration
{
    /// <inheritdoc />
    protected override string ScriptName => "0002_PeerSshPublicKey";
}
