using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RCynQ.Core.Data.Entities;

namespace RCynQ.Core.Data.Context;

/// <summary>
/// EF Core context of the local node database.
/// </summary>
/// <remarks>
/// The schema is owned by FluentMigrator (<c>RCynQ.Core.Data.Migrations.SQLite</c>); this context never creates
/// tables. <see cref="DateTimeOffset"/> values are stored as UTC ticks (<c>INTEGER</c>) so they can be ordered in SQLite.
/// </remarks>
public sealed class RCynQDbContext : DbContext
{
    /// <summary>
    /// Converts <see cref="DateTimeOffset"/> values to UTC ticks and back.
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicksConverter = new(
        value => value.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    /// <summary>
    /// Initializes a new instance of the <see cref="RCynQDbContext"/> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    public RCynQDbContext(DbContextOptions<RCynQDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the nodes set.</summary>
    public DbSet<NodeEntity> Nodes => Set<NodeEntity>();

    /// <summary>Gets the peers set.</summary>
    public DbSet<PeerEntity> Peers => Set<PeerEntity>();

    /// <summary>Gets the chain links set.</summary>
    public DbSet<ChainLinkEntity> ChainLinks => Set<ChainLinkEntity>();

    /// <summary>Gets the snapshots set.</summary>
    public DbSet<SnapshotEntity> Snapshots => Set<SnapshotEntity>();

    /// <summary>Gets the patch metadata set.</summary>
    public DbSet<PatchEnvelopeEntity> PatchEnvelopes => Set<PatchEnvelopeEntity>();

    /// <summary>Gets the sync history set.</summary>
    public DbSet<SyncHistoryEntity> SyncHistory => Set<SyncHistoryEntity>();

    /// <summary>Gets the secrets set.</summary>
    public DbSet<SecretEntity> Secrets => Set<SecretEntity>();

    /// <summary>Gets the diagnostics runs set.</summary>
    public DbSet<DiagnosticRunEntity> DiagnosticRuns => Set<DiagnosticRunEntity>();

    /// <summary>
    /// Configures table names, keys, relations and value conversions.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeEntity>(entity =>
        {
            entity.ToTable("Nodes");
            entity.HasKey(node => node.Id);
        });

        modelBuilder.Entity<PeerEntity>(entity =>
        {
            entity.ToTable("Peers");
            entity.HasKey(peer => peer.Id);
        });

        modelBuilder.Entity<ChainLinkEntity>(entity =>
        {
            entity.ToTable("ChainLinks");
            entity.HasKey(link => link.Id);
            entity.HasIndex(link => link.Name).IsUnique();
            entity.HasOne(link => link.Peer)
                .WithMany()
                .HasForeignKey(link => link.PeerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SnapshotEntity>(entity =>
        {
            entity.ToTable("Snapshots");
            entity.HasKey(snapshot => snapshot.Id);
        });

        modelBuilder.Entity<PatchEnvelopeEntity>(entity =>
        {
            entity.ToTable("PatchEnvelopes");
            entity.HasKey(patch => patch.Id);
        });

        modelBuilder.Entity<SyncHistoryEntity>(entity =>
        {
            entity.ToTable("SyncHistory");
            entity.HasKey(entry => entry.Id);
        });

        modelBuilder.Entity<SecretEntity>(entity =>
        {
            entity.ToTable("Secrets");
            entity.HasKey(secret => secret.Name);
        });

        modelBuilder.Entity<DiagnosticRunEntity>(entity =>
        {
            entity.ToTable("DiagnosticRuns");
            entity.HasKey(run => run.Id);
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(UtcTicksConverter);
                }
            }
        }
    }
}
