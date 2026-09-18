using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Leagues;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="Membership"/> to <c>Memberships</c>.</summary>
public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Memberships");
        builder.HasKey(membership => membership.Id);

        builder.Ignore(membership => membership.IsActive);

        builder.Property(membership => membership.Role).HasConversion<byte>();
        builder.Property(membership => membership.DisplayNameOverride)
            .HasMaxLength(Membership.DisplayNameMaxLength);

        builder.HasOne(membership => membership.League)
            .WithMany()
            .HasForeignKey(membership => membership.LeagueId)
            .IsRequired();

        builder.HasOne(membership => membership.User)
            .WithMany()
            .HasForeignKey(membership => membership.UserId)
            .IsRequired();

        builder.HasIndex(membership => new { membership.LeagueId, membership.UserId }).IsUnique();

        // Leaderboards and the authorization filter both scan a league's active memberships.
        builder.HasIndex(membership => new { membership.LeagueId, membership.RemovedUtc });
        builder.HasIndex(membership => membership.UserId);
    }
}
