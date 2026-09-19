namespace NcaafPickEm.Domain.Notifications;

/// <summary>
/// One outstanding retry of a push that failed for a reason worth trying again (Feature 11
/// delivery policy; 04-Domain-Algorithms.md section 11).
/// </summary>
/// <remarks>
/// The retry intent is persisted rather than held in a <c>Task.Delay</c>: a restart between the
/// first failure and the third attempt must not silently drop the notification, and the job
/// scheduler (P0-06) already knows how to run something whose due time lives in the database.
/// <c>PushRetryJob</c> drains this table; a row is deleted as soon as the attempt succeeds, the
/// subscription turns out to be gone, or the third attempt fails.
/// </remarks>
public sealed class PushRetry
{
    /// <summary>Maximum length of <see cref="PayloadJson"/>, in characters.</summary>
    public const int PayloadJsonMaxLength = 1000;

    public Guid Id { get; set; }

    /// <summary>The device the retry is aimed at.</summary>
    public Guid SubscriptionId { get; set; }

    public PushSubscription? Subscription { get; set; }

    /// <summary>The <c>NotificationLog</c> row this delivery is recorded in.</summary>
    public Guid NotificationLogId { get; set; }

    public NotificationLogEntry? NotificationLogEntry { get; set; }

    /// <summary>The payload as it was first serialized, so retries send the identical message.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>The TTL the original send used, in seconds.</summary>
    public int TtlSeconds { get; set; }

    /// <summary>How many retries have already been made. 0 means none yet; 3 is the last.</summary>
    public int Attempt { get; set; }

    /// <summary>When the next retry becomes due.</summary>
    public DateTime NextAttemptUtc { get; set; }

    /// <summary>When the first send failed. The backoff schedule is measured from here.</summary>
    public DateTime CreatedUtc { get; set; }
}
