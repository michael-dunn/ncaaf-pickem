namespace NcaafPickEm.Infrastructure.Jobs.Refresh;

/// <summary>
/// Daily schedule refresh, Wednesday through Saturday, for a status change (a postponement or a
/// kickoff-time correction) that lands on CFBD after Tuesday's weekly refresh (Feature 09, 13).
/// Runs the same work as <see cref="ScheduleRefreshJob"/> through the shared
/// <see cref="ScheduleRefreshRunner"/>; it is a separate registration only because its cron
/// occurrences are on different days and times.
/// </summary>
public sealed class ScheduleRefreshDailyJob : IScheduledJob
{
    private readonly ScheduleRefreshRunner _runner;

    /// <summary>Creates the job.</summary>
    public ScheduleRefreshDailyJob(ScheduleRefreshRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name => "ScheduleRefreshDaily";

    /// <inheritdoc />
    public string CronExpression => "0 4 * * 3-6";

    /// <inheritdoc />
    public Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken) =>
        _runner.RunAsync(cancellationToken);
}
