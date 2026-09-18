using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Leagues;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="League"/> to <c>Leagues</c>.</summary>
public sealed class LeagueConfiguration : IEntityTypeConfiguration<League>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<League> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Leagues");
        builder.HasKey(league => league.Id);

        builder.Property(league => league.Name).HasMaxLength(League.NameMaxLength).IsRequired();
        builder.Property(league => league.DefaultPointValue).HasDefaultValue(10);

        builder.HasOne(league => league.CreatedByUser)
            .WithMany()
            .HasForeignKey(league => league.CreatedByUserId)
            .IsRequired();

        builder.HasIndex(league => league.SeasonYear);
    }
}
