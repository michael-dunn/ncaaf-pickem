using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="NotificationLogEntry"/> to <c>NotificationLog</c>.</summary>
public sealed class NotificationLogEntryConfiguration : IEntityTypeConfiguration<NotificationLogEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificationLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("NotificationLog");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Type).HasConversion<byte>();
        builder.Property(entry => entry.Result).HasConversion<byte>();
        builder.Property(entry => entry.Error).HasMaxLength(NotificationLogEntry.ErrorMaxLength);

        builder.HasOne(entry => entry.User)
            .WithMany()
            .HasForeignKey(entry => entry.UserId)
            .IsRequired();

        builder.HasOne(entry => entry.League)
            .WithMany()
            .HasForeignKey(entry => entry.LeagueId)
            .IsRequired();

        builder.HasOne<PushSubscription>()
            .WithMany()
            .HasForeignKey(entry => entry.SubscriptionId);

        // "Once per week per reminder type" is a database guarantee, not a job-code guarantee:
        // a re-run of a reminder job hits this index instead of sending twice.
        builder.HasIndex(entry => new { entry.UserId, entry.LeagueId, entry.Week, entry.Type })
            .IsUnique()
            .HasFilter(
                $"[Type] IN ({(byte)NotificationType.FridayReminder}, " +
                $"{(byte)NotificationType.CommissionerSummary}, " +
                $"{(byte)NotificationType.SaturdayReminder})")
            .HasDatabaseName("UX_NotificationLog_WeeklyReminderOncePerWeek");

        builder.HasIndex(entry => new { entry.LeagueId, entry.CreatedUtc })
            .IsDescending(false, true);
    }
}
