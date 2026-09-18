using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Operations;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="UnmatchedGame"/> to <c>UnmatchedGames</c>.</summary>
public sealed class UnmatchedGameConfiguration : IEntityTypeConfiguration<UnmatchedGame>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UnmatchedGame> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UnmatchedGames");
        builder.HasKey(game => game.Id);

        builder.Property(game => game.Source).HasConversion<byte>();
        builder.Property(game => game.RawHomeName).HasMaxLength(UnmatchedGame.RawNameMaxLength).IsRequired();
        builder.Property(game => game.RawAwayName).HasMaxLength(UnmatchedGame.RawNameMaxLength).IsRequired();
        builder.Property(game => game.RawPayload).IsRequired();

        // The data status page lists what is still unresolved.
        builder.HasIndex(game => new { game.ResolvedUtc, game.GameDate });
    }
}
