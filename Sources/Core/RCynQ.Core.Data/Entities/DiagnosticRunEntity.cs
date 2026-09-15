namespace RCynQ.Core.Data.Entities;

/// <summary>
/// EF Core entity for a diagnostics run (table <c>DiagnosticRuns</c>).
/// </summary>
public sealed class DiagnosticRunEntity
{
    /// <summary>Gets or sets the run identifier (primary key).</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the time of the run.</summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>Gets or sets the role the checks were selected for.</summary>
    public int Role { get; set; }

    /// <summary>Gets or sets a value indicating whether all checks passed.</summary>
    public bool AllPassed { get; set; }

    /// <summary>Gets or sets the JSON list of results.</summary>
    public string ResultsJson { get; set; } = "[]";
}
