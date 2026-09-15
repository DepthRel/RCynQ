using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Data.Context;
using RCynQ.Core.Data.Entities;
using RCynQ.Core.Data.Mapping;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.State;

namespace RCynQ.Core.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDiagnosticRunRepository"/>.
/// </summary>
public sealed class DiagnosticRunRepository : IDiagnosticRunRepository
{
    /// <summary>
    /// The context factory.
    /// </summary>
    private readonly IDbContextFactory<RCynQDbContext> _contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticRunRepository"/> class.
    /// </summary>
    /// <param name="contextFactory">The context factory.</param>
    public DiagnosticRunRepository(IDbContextFactory<RCynQDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task AddAsync(DiagnosticsEvent run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        db.DiagnosticRuns.Add(new DiagnosticRunEntity
        {
            Id = Guid.NewGuid(),
            TimestampUtc = run.TimestampUtc,
            Role = (int)run.Role,
            AllPassed = run.AllPassed,
            ResultsJson = JsonSerializer.Serialize(run.Results, EntityMapper.Json)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiagnosticsEvent>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.DiagnosticRuns.AsNoTracking().OrderByDescending(run => run.TimestampUtc).Take(count).ToListAsync(cancellationToken);
        return entities
            .Select(entity => new DiagnosticsEvent(
                (NodeRole)entity.Role,
                JsonSerializer.Deserialize<List<DiagnosticResult>>(entity.ResultsJson, EntityMapper.Json) ?? new List<DiagnosticResult>(),
                entity.TimestampUtc))
            .ToArray();
    }
}
