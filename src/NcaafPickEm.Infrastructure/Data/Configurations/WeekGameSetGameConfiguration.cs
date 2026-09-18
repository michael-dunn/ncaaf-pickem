using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="WeekGameSetGame"/> to <c>WeekGameSetGames</c>.</summary>
public sealed class WeekGameSetGameConfiguration : IEntityTypeConfiguration<WeekGameSetGame>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WeekGameSetGame> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("WeekGameSetGames");
        builder.HasKey(game => game.Id);

        builder.Ignore(game => game.IsActive);

        builder.Property(game => game.Source).HasConversion<byte>();
        builder.Property(game => game.RemovedReason).HasMaxLength(WeekGameSetGame.RemovedReasonMaxLength);

        builder.HasOne(game => game.WeekGameSet)
            .WithMany()
            .HasForeignKey(game => game.WeekGameSetId)
            .IsRequired();

        builder.HasOne(game => game.Game)
            .WithMany()
            .HasForeignKey(game => game.GameId)
            .IsRequired();

        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(game => game.ResultOverrideWinnerTeamId);

        builder.HasIndex(game => new { game.WeekGameSetId, game.GameId }).IsUnique();

        // Score updates fan out from a Game to every league that picked it.
        builder.HasIndex(game => game.GameId);
    }
}
