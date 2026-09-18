using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="GameLine"/> to <c>GameLines</c>.</summary>
public sealed class GameLineConfiguration : IEntityTypeConfiguration<GameLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GameLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GameLines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.Provider).HasMaxLength(GameLine.ProviderMaxLength).IsRequired();

        builder.HasOne(line => line.Game)
            .WithMany()
            .HasForeignKey(line => line.GameId)
            .IsRequired();

        // "Current line" is the newest row for a game, so the index is ordered to answer it.
        builder.HasIndex(line => new { line.GameId, line.FetchedUtc })
            .IsDescending(false, true);
    }
}
