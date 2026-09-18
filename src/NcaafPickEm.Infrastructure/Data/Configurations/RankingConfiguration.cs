using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="Ranking"/> to <c>Rankings</c>.</summary>
public sealed class RankingConfiguration : IEntityTypeConfiguration<Ranking>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Ranking> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Rankings");
        builder.HasKey(ranking => new { ranking.SeasonYear, ranking.Week, ranking.Poll, ranking.Rank });

        builder.Property(ranking => ranking.Poll).HasMaxLength(Ranking.PollMaxLength).IsRequired();

        builder.HasOne(ranking => ranking.Team)
            .WithMany()
            .HasForeignKey(ranking => ranking.TeamId)
            .IsRequired();

        // "Is this team ranked this week?" during game set generation.
        builder.HasIndex(ranking => new { ranking.SeasonYear, ranking.Week, ranking.TeamId });
    }
}
