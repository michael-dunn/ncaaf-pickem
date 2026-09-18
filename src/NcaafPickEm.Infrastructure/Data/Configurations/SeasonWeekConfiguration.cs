using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="SeasonWeek"/> to <c>SeasonWeeks</c>.</summary>
public sealed class SeasonWeekConfiguration : IEntityTypeConfiguration<SeasonWeek>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SeasonWeek> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SeasonWeeks");
        builder.HasKey(week => new { week.SeasonYear, week.Week });
    }
}
