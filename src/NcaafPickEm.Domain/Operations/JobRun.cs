namespace NcaafPickEm.Domain.Operations;

/// <summary>
/// One execution of one scheduled job (Feature 10). The unique key
/// <c>(JobName, ScheduledForUtc)</c> is what makes cron jobs idempotent across restarts (D-005).
/// </summary>
public sealed class JobRun
{
    /// <summary>Maximum length of <see cref="JobName"/>, in characters.</summary>
    public const int JobNameMaxLength = 60;

    /// <summary>Maximum length of <see cref="Error"/>, in characters.</summary>
    public const int ErrorMaxLength = 1000;

    public Guid Id { get; set; }

    public string JobName { get; set; } = string.Empty;

    /// <summary>The cron occurrence this run belongs to, not the moment it actually started.</summary>
    public DateTime ScheduledForUtc { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime? FinishedUtc { get; set; }

    public bool Success { get; set; }

    public string? Error { get; set; }
}
