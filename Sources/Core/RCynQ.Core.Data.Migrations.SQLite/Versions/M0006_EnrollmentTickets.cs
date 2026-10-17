using FluentMigrator;
using RCynQ.Core.Data.Migrations.Base;

namespace RCynQ.Core.Data.Migrations.SQLite.Versions;

/// <summary>
/// Migration 0006: keeps the tickets of the invitations this node created (<c>Scripts/0006_EnrollmentTickets.Up.sql</c>),
/// so a node invited by it can enroll once; removed with <c>Scripts/0006_EnrollmentTickets.Down.sql</c>.
/// </summary>
[Migration(6, "Enrollment tickets")]
public sealed class M0006_EnrollmentTickets : ScriptMigration
{
    /// <inheritdoc />
    protected override string ScriptName => "0006_EnrollmentTickets";
}
