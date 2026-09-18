namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// Outcome of one attempted push delivery (Feature 11).
/// </summary>
public enum NotificationResult : byte
{
    /// <summary>The push service accepted the message.</summary>
    Sent = 0,

    /// <summary>The push service rejected the message; see <c>Error</c>.</summary>
    Failed = 1,

    /// <summary>The subscription is gone (404/410); it is deleted after this.</summary>
    Expired = 2,

    /// <summary>Nothing was sent: the member had nothing to be reminded about, or had no subscription.</summary>
    Skipped = 3,
}
