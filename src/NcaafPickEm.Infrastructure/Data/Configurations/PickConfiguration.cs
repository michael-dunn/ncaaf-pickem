using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Picks;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="Pick"/> to <c>Picks</c>.</summary>
public sealed class PickConfiguration : IEntityTypeConfiguration<Pick>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Pick> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Picks");
        builder.HasKey(pick => pick.Id);

        builder.HasOne(pick => pick.Membership)
            .WithMany()
            .HasForeignKey(pick => pick.MembershipId)
            .IsRequired();

        builder.HasOne(pick => pick.WeekGameSetGame)
            .WithMany()
            .HasForeignKey(pick => pick.WeekGameSetGameId)
            .IsRequired();

        builder.HasOne(pick => pick.PickedTeam)
            .WithMany()
            .HasForeignKey(pick => pick.PickedTeamId)
            .IsRequired();

        builder.HasIndex(pick => new { pick.MembershipId, pick.WeekGameSetGameId }).IsUnique();

        // The dashboard and the week grid read every pick on a game.
        builder.HasIndex(pick => pick.WeekGameSetGameId);
    }
}
