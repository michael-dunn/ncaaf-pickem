namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// The <c>Jobs</c> configuration section (<c>Jobs__Enabled</c>, <c>Jobs__CatchUpMinutes</c>).
/// </summary>
public sealed class JobsOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Jobs";

    /// <summary>
    /// Whether the background scheduler runs at all. False in API tests and useful on a second
    /// instance that must not duplicate the first one's work.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How far back a tick will look for occurrences a previous run missed, in minutes.
    /// </summary>
    /// <remarks>
    /// Bounds the replay after an outage: a machine that was off for a day comes back and runs
    /// the last hour of schedule, not the whole day. See the scheduler decision in DECISIONS.md.
    /// </remarks>
    public int CatchUpMinutes { get; set; } = 60;
}
