using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Notifications;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="PushSubscription"/> to <c>PushSubscriptions</c>.</summary>
public sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PushSubscriptions");
        builder.HasKey(subscription => subscription.Id);

        builder.Property(subscription => subscription.Endpoint)
            .HasMaxLength(PushSubscription.EndpointMaxLength)
            .IsRequired();

        // nvarchar(2048) is 4096 bytes and SQL Server caps a nonclustered index key at 1700, so
        // the unique constraint the data model asks for rides on a persisted hash instead.
        builder.Property(subscription => subscription.EndpointHash)
            .HasColumnType("binary(32)")
            .HasComputedColumnSql("CONVERT(binary(32), HASHBYTES('SHA2_256', [Endpoint]))", stored: true);

        builder.Property(subscription => subscription.P256dh).HasMaxLength(200).IsRequired();
        builder.Property(subscription => subscription.Auth).HasMaxLength(100).IsRequired();
        builder.Property(subscription => subscription.UserAgent).HasMaxLength(300);

        builder.HasOne(subscription => subscription.User)
            .WithMany()
            .HasForeignKey(subscription => subscription.UserId)
            .IsRequired();

        builder.HasIndex(subscription => subscription.EndpointHash).IsUnique();
        builder.HasIndex(subscription => subscription.UserId);
    }
}
