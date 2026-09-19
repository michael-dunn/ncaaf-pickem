namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// What one delivery attempt to one device did (Feature 11 delivery policy,
/// 04-Domain-Algorithms.md section 11).
/// </summary>
/// <param name="Success">The push service accepted the message.</param>
/// <param name="StatusCode">HTTP status the push service answered with, when there was one.</param>
/// <param name="Error">Why it failed, short enough for <c>NotificationLog.Error</c>.</param>
/// <param name="SubscriptionGone">
/// The subscription can never work again (404/410, or keys the library cannot encrypt for), so the
/// caller deletes the row and logs <c>Expired</c> instead of retrying.
/// </param>
public readonly record struct PushSendResult(bool Success, int? StatusCode, string? Error, bool SubscriptionGone)
{
    /// <summary>The push service accepted the message.</summary>
    /// <param name="statusCode">The 2xx it answered with.</param>
    public static PushSendResult Accepted(int? statusCode = null) => new(true, statusCode, null, false);

    /// <summary>The subscription is dead; delete it.</summary>
    /// <param name="error">Why.</param>
    /// <param name="statusCode">404 or 410, when the push service said so.</param>
    public static PushSendResult Gone(string error, int? statusCode = null) => new(false, statusCode, error, true);

    /// <summary>A failure worth retrying.</summary>
    /// <param name="error">Why.</param>
    /// <param name="statusCode">The status, when there was one.</param>
    public static PushSendResult Failed(string error, int? statusCode = null) => new(false, statusCode, error, false);
}
