using FluentMigrator;
using RCynQ.Core.Data.Migrations.Base;

namespace RCynQ.Core.Data.Migrations.SQLite.Versions;

/// <summary>
/// Migration 0007: stores the commits a patch delivers instead of files (<c>Scripts/0007_PatchCommits.Up.sql</c>), so the
/// history tells a delivery of commits from a patch of files and lists its commits; removed with
/// <c>Scripts/0007_PatchCommits.Down.sql</c>.
/// </summary>
[Migration(7, "Patch commits")]
public sealed class M0007_PatchCommits : ScriptMigration
{
    /// <inheritdoc />
    protected override string ScriptName => "0007_PatchCommits";
}
