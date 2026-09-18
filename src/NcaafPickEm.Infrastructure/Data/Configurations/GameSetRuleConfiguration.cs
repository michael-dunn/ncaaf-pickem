using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.GameSets;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="GameSetRule"/> to <c>GameSetRules</c>.</summary>
public sealed class GameSetRuleConfiguration : IEntityTypeConfiguration<GameSetRule>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GameSetRule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GameSetRules");
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

        // Generation reads either the default rows (Week null) or one week's override rows.
        builder.HasIndex(rule => new { rule.LeagueId, rule.Week, rule.SortOrder });
    }
}
