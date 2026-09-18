using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Scoring;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="SeasonStandingsSnapshot"/> to <c>SeasonStandingsSnapshots</c>.</summary>
public sealed class SeasonStandingsSnapshotConfiguration : IEntityTypeConfiguration<SeasonStandingsSnapshot>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SeasonStandingsSnapshot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SeasonStandingsSnapshots");
        builder.HasKey(snapshot => new { snapshot.LeagueId, snapshot.ThroughWeek, snapshot.MembershipId });

        builder.HasOne(snapshot => snapshot.League)
            .WithMany()
            .HasForeignKey(snapshot => snapshot.LeagueId)
            .IsRequired();

        builder.HasOne(snapshot => snapshot.Membership)
            .WithMany()
            .HasForeignKey(snapshot => snapshot.MembershipId)
            .IsRequired();

        builder.HasIndex(snapshot => snapshot.MembershipId);
    }
}
