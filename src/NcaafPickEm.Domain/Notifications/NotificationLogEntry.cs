using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Notifications;

/// <summary>
/// One attempted notification (Feature 11). Maps to the <c>NotificationLog</c> table.
/// </summary>
/// <remarks>
/// A filtered unique index on <c>(UserId, LeagueId, Week, Type)</c> covering the three weekly
/// reminder types is what makes "once per week" true even if a job runs twice.
/// </remarks>
public sealed class NotificationLogEntry
{
    /// <summary>Maximum length of <see cref="Error"/>, in characters.</summary>
    public const int ErrorMaxLength = 500;

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public Guid LeagueId { get; set; }

    public League? League { get; set; }

    public int Week { get; set; }

    public NotificationType Type { get; set; }

    /// <summary>The device this went to. Null when nothing was sent.</summary>
    public Guid? SubscriptionId { get; set; }

    public NotificationResult Result { get; set; }

    public string? Error { get; set; }

    public DateTime CreatedUtc { get; set; }
}
