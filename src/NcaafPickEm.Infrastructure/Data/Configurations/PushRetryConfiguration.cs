using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Notifications;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="PushRetry"/> to <c>PushRetries</c> (P7-01).</summary>
public sealed class PushRetryConfiguration : IEntityTypeConfiguration<PushRetry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PushRetry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PushRetries");
        builder.HasKey(retry => retry.Id);

        builder.Property(retry => retry.PayloadJson)
            .HasMaxLength(PushRetry.PayloadJsonMaxLength)
            .IsRequired();

        builder.HasOne(retry => retry.Subscription)
            .WithMany()
            .HasForeignKey(retry => retry.SubscriptionId)
            .IsRequired();

        builder.HasOne(retry => retry.NotificationLogEntry)
            .WithMany()
            .HasForeignKey(retry => retry.NotificationLogId)
            .IsRequired();

        // The one query the job makes: "what is due now?".
        builder.HasIndex(retry => retry.NextAttemptUtc);

        // A device has at most one outstanding retry per notification, so a job that runs twice
        // cannot queue the same message twice.
        builder.HasIndex(retry => new { retry.NotificationLogId, retry.SubscriptionId }).IsUnique();
    }
}
