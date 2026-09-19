namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// "Retried up to 3 times over 15 minutes, then logged" (Feature 11 delivery;
/// 04-Domain-Algorithms.md section 11), expressed as offsets from the first failure.
/// </summary>
public static class PushRetryPolicy
{
    /// <summary>
    /// When each retry is due, measured from the moment the first send failed: +1, +5, +15
    /// minutes. A quick first retry catches a blip; the last one lands on the fifteen-minute
    /// boundary the story names.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    /// <summary>How many retries follow the original attempt before the send is Failed.</summary>
    public static int MaxAttempts => Backoff.Count;

    /// <summary>
    /// When retry number <paramref name="attempt"/> (0-based) is due, or null when the attempts
    /// are exhausted.
    /// </summary>
    /// <param name="firstFailureUtc">When the original send failed.</param>
    /// <param name="attempt">How many retries have already been made.</param>
    public static DateTime? NextAttemptUtc(DateTime firstFailureUtc, int attempt) =>
        attempt >= 0 && attempt < Backoff.Count ? firstFailureUtc + Backoff[attempt] : null;
}
