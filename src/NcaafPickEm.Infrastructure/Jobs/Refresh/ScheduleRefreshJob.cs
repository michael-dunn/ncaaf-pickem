namespace NcaafPickEm.Infrastructure.Jobs.Refresh;

/// <summary>
/// Weekly schedule refresh, right after <see cref="TeamsRefreshJob"/>: the roster is settled, so
/// this is when a mid-week schedule shuffle (byes announced, kickoff times set) is next expected
/// to have landed on CFBD (Feature 09, 13).
/// </summary>
public sealed class ScheduleRefreshJob : IScheduledJob
{
    private readonly ScheduleRefreshRunner _runner;

    /// <summary>Creates the job.</summary>
    public ScheduleRefreshJob(ScheduleRefreshRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name => "ScheduleRefresh";

    /// <inheritdoc />
    public string CronExpression => "10 3 * * 2";

    /// <inheritdoc />
    public Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken) =>
        _runner.RunAsync(cancellationToken);
}
