using FluentMigrator;
using RCynQ.Core.Data.Migrations.Base;

namespace RCynQ.Core.Data.Migrations.SQLite.Versions;

/// <summary>
/// Migration 0008: stores the choices of a part of the changes of files (<c>Scripts/0008_PartialChanges.Up.sql</c>), so
/// only the chosen changes of a file go to the other node; removed with <c>Scripts/0008_PartialChanges.Down.sql</c>.
/// </summary>
[Migration(8, "Partial changes")]
public sealed class M0008_PartialChanges : ScriptMigration
{
    /// <inheritdoc />
    protected override string ScriptName => "0008_PartialChanges";
}
