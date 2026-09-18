using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.GameSets;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="WeekGameSet"/> to <c>WeekGameSets</c>.</summary>
public sealed class WeekGameSetConfiguration : IEntityTypeConfiguration<WeekGameSet>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WeekGameSet> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("WeekGameSets");
        builder.HasKey(set => set.Id);

        builder.Ignore(set => set.IsLocked);

        builder.HasOne(set => set.League)
            .WithMany()
            .HasForeignKey(set => set.LeagueId)
            .IsRequired();

        builder.HasIndex(set => new { set.LeagueId, set.Week }).IsUnique();

        // The lock job sweeps every set whose lock time has passed and that is not locked yet.
        builder.HasIndex(set => new { set.LockAtUtc, set.LockedUtc });
    }
}
