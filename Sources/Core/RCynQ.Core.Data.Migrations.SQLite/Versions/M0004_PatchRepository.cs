using FluentMigrator;
using RCynQ.Core.Data.Migrations.Base;

namespace RCynQ.Core.Data.Migrations.SQLite.Versions;

/// <summary>
/// Migration 0004: stores the name of the repository a patch belongs to (<c>Scripts/0004_PatchRepository.Up.sql</c>), so
/// a node that works with several repositories shows the patches of each one separately; removed with
/// <c>Scripts/0004_PatchRepository.Down.sql</c>.
/// </summary>
[Migration(4, "Patch repository")]
public sealed class M0004_PatchRepository : ScriptMigration
{
    /// <inheritdoc />
    protected override string ScriptName => "0004_PatchRepository";
}
