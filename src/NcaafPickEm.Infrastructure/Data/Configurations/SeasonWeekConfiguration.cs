using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps the <see cref="SeasonWeek"/> domain record to <c>SeasonWeeks</c>.</summary>
/// <remarks>
/// <see cref="SeasonWeek"/> is the pure domain record the season calendar works with, so its
/// window is <see cref="DateTimeOffset"/>. The table keeps the data model's <c>datetime2</c> UTC
/// columns; the conversion below bridges the two without a schema change.
/// </remarks>
public sealed class SeasonWeekConfiguration : IEntityTypeConfiguration<SeasonWeek>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SeasonWeek> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SeasonWeeks");
        builder.HasKey(week => new { week.SeasonYear, week.Week });

        builder.Property(week => week.StartUtc)
            .HasConversion(
                value => value.UtcDateTime,
                value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)))
            .HasColumnType("datetime2");

        builder.Property(week => week.EndUtc)
            .HasConversion(
                value => value.UtcDateTime,
                value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)))
            .HasColumnType("datetime2");
    }
}
