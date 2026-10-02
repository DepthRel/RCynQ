using FluentMigrator;
using RCynQ.Core.Data.Migrations.Base;

namespace RCynQ.Core.Data.Migrations.SQLite.Versions;

/// <summary>
/// Migration 0005: stores the addresses the peer of a link is reachable at and the one chosen by hand
/// (<c>Scripts/0005_LinkRoutes.Up.sql</c>), so a node can go another way when the usual one does not answer; removed
/// with <c>Scripts/0005_LinkRoutes.Down.sql</c>.
/// </summary>
[Migration(5, "Link routes")]
public sealed class M0005_LinkRoutes : ScriptMigration
{
    /// <inheritdoc />
    protected override string ScriptName => "0005_LinkRoutes";
}
