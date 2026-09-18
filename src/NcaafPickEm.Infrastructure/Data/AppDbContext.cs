using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Points;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data.Conversions;

namespace NcaafPickEm.Infrastructure.Data;

/// <summary>
/// The one database context. Every table in <c>02-Data-Model.md</c> lives here; mapping is done
/// entirely in <c>Data/Configurations/&lt;Entity&gt;Configuration.cs</c> so the Domain project
/// stays free of EF attributes (05-Conventions.md).
/// </summary>
/// <remarks>
/// This type is a shared hot spot. Adding a table means adding a <see cref="DbSet{TEntity}"/>
/// here and one configuration file; do not put mapping logic in this class.
/// </remarks>
public class AppDbContext : DbContext
{
    /// <summary>Creates the context. The provider and connection string come from options.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // Identity (Feature 08)
    public DbSet<User> Users => Set<User>();

    // Leagues (Features 01, 13)
    public DbSet<League> Leagues => Set<League>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Invite> Invites => Set<Invite>();

    // Season reference data (Features 09, 13)
    public DbSet<SeasonWeek> SeasonWeeks => Set<SeasonWeek>();
    public DbSet<Conference> Conferences => Set<Conference>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamAlias> TeamAliases => Set<TeamAlias>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameLine> GameLines => Set<GameLine>();
    public DbSet<Ranking> Rankings => Set<Ranking>();

    // League configuration (Features 02, 03)
    public DbSet<GameSetRule> GameSetRules => Set<GameSetRule>();
    public DbSet<PointRule> PointRules => Set<PointRule>();
    public DbSet<WeekGameSet> WeekGameSets => Set<WeekGameSet>();
    public DbSet<WeekGameSetGame> WeekGameSetGames => Set<WeekGameSetGame>();

    // Picks (Feature 04)
    public DbSet<Pick> Picks => Set<Pick>();
    public DbSet<WeekSubmission> WeekSubmissions => Set<WeekSubmission>();

    // Scoring (Features 06, 07)
    public DbSet<WeekResult> WeekResults => Set<WeekResult>();
    public DbSet<SeasonStandingsSnapshot> SeasonStandingsSnapshots => Set<SeasonStandingsSnapshot>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    // Notifications (Feature 11)
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<NotificationLogEntry> NotificationLog => Set<NotificationLogEntry>();

    // Operations (Features 09, 12, 10)
    public DbSet<ProviderCall> ProviderCalls => Set<ProviderCall>();
    public DbSet<DataRefreshStatus> DataRefreshStatuses => Set<DataRefreshStatus>();
    public DbSet<UnmatchedGame> UnmatchedGames => Set<UnmatchedGame>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // Every DateTime column is datetime2 holding UTC (02-Data-Model.md). The converter also
        // stamps Kind=Utc on read so nothing downstream can mistake a value for local time (D-014).
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>()
            .HaveColumnType("datetime2");

        // Spreads and point thresholds are half-point precision.
        configurationBuilder.Properties<decimal>()
            .HavePrecision(5, 1);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Guids are created in code with Guid.CreateVersion7() (05-Conventions.md), so SQL
            // Server must never generate one.
            foreach (IMutableProperty key in entityType.GetDeclaredKeys().SelectMany(k => k.Properties))
            {
                if (key.ClrType == typeof(Guid))
                {
                    key.ValueGenerated = ValueGenerated.Never;
                }
            }

            // Nothing in this app hard-deletes a principal row (removals are soft), and cascading
            // FKs on a graph this connected trip SQL Server's multiple-cascade-path rule. Restrict
            // everywhere makes an accidental delete fail loudly instead of taking history with it.
            foreach (IMutableForeignKey foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }
}
