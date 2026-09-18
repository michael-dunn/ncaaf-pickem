using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Points;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="PointRule"/> to <c>PointRules</c>.</summary>
public sealed class PointRuleConfiguration : IEntityTypeConfiguration<PointRule>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PointRule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PointRules");
        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.RuleType).HasConversion<byte>();

        builder.HasOne(rule => rule.League)
            .WithMany()
            .HasForeignKey(rule => rule.LeagueId)
            .IsRequired();

        builder.HasOne(rule => rule.Conference)
            .WithMany()
            .HasForeignKey(rule => rule.ConferenceId);

        builder.HasOne(rule => rule.Team)
            .WithMany()
            .HasForeignKey(rule => rule.TeamId);

        // Resolution walks a league's rules in priority order.
        builder.HasIndex(rule => new { rule.LeagueId, rule.Priority });
    }
}
