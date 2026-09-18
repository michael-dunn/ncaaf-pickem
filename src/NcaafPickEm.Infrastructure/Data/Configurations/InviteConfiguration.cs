using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Leagues;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="Invite"/> to <c>Invites</c>.</summary>
public sealed class InviteConfiguration : IEntityTypeConfiguration<Invite>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Invite> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Invites");
        builder.HasKey(invite => invite.Id);

        builder.Property(invite => invite.Code).HasMaxLength(Invite.CodeMaxLength).IsRequired();
        builder.Property(invite => invite.MaxUses).HasDefaultValue(Invite.DefaultMaxUses);
        builder.Property(invite => invite.Uses).HasDefaultValue(0);

        builder.HasOne(invite => invite.League)
            .WithMany()
            .HasForeignKey(invite => invite.LeagueId)
            .IsRequired();

        builder.HasOne(invite => invite.CreatedByMembership)
            .WithMany()
            .HasForeignKey(invite => invite.CreatedByMembershipId)
            .IsRequired();

        builder.HasIndex(invite => invite.Code).IsUnique();
        builder.HasIndex(invite => invite.LeagueId);
    }
}
