using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Scoring;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="WeekResult"/> to <c>WeekResults</c>.</summary>
public sealed class WeekResultConfiguration : IEntityTypeConfiguration<WeekResult>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WeekResult> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("WeekResults");
        builder.HasKey(result => new { result.MembershipId, result.WeekGameSetId });

        builder.HasOne(result => result.Membership)
            .WithMany()
            .HasForeignKey(result => result.MembershipId)
            .IsRequired();

        builder.HasOne(result => result.WeekGameSet)
            .WithMany()
            .HasForeignKey(result => result.WeekGameSetId)
            .IsRequired();

        // The weekly leaderboard reads one week across every member.
        builder.HasIndex(result => result.WeekGameSetId);
    }
}
