using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="Game"/> to <c>Games</c>.</summary>
public sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Games");
        builder.HasKey(game => game.Id);

        builder.Property(game => game.Status).HasConversion<byte>();
        builder.Property(game => game.Clock).HasMaxLength(Game.ClockMaxLength);
        builder.Property(game => game.Venue).HasMaxLength(Game.VenueMaxLength);

        builder.HasOne(game => game.HomeTeam)
            .WithMany()
            .HasForeignKey(game => game.HomeTeamId)
            .IsRequired();

        builder.HasOne(game => game.AwayTeam)
            .WithMany()
            .HasForeignKey(game => game.AwayTeamId)
            .IsRequired();

        builder.HasIndex(game => game.CfbdGameId).IsUnique();

        // Nullable until the ESPN matcher runs, so the uniqueness has to be filtered: SQL Server
        // would otherwise allow only one unmatched game in the whole table.
        builder.HasIndex(game => game.EspnEventId)
            .IsUnique()
            .HasFilter("[EspnEventId] IS NOT NULL");

        // Game set generation: Saturday FBS games for one week.
        builder.HasIndex(game => new { game.SeasonYear, game.Week, game.IsSaturdayEastern });

        // The Saturday poller only ever looks at games that are not finished.
        builder.HasIndex(game => game.Status)
            .HasFilter($"[Status] IN ({(byte)GameStatus.Scheduled}, {(byte)GameStatus.InProgress})");
    }
}
