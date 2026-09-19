namespace NcaafPickEm.Infrastructure.Jobs.Refresh;

/// <summary>
/// A third, early-Tuesday rankings refresh alongside <see cref="TeamsRefreshJob"/> and
/// <see cref="ScheduleRefreshJob"/>'s Tuesday-morning slot, in case the poll shifted again over
/// Monday night (Feature 09).
/// </summary>
public sealed class RankingsRefreshTuesdayJob : IScheduledJob
{
    private readonly RankingsRefreshRunner _runner;

    /// <summary>Creates the job.</summary>
    public RankingsRefreshTuesdayJob(RankingsRefreshRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name => "RankingsRefreshTuesday";

    /// <inheritdoc />
    public string CronExpression => "20 3 * * 2";

    /// <inheritdoc />
    public Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken) =>
        _runner.RunAsync(cancellationToken);
}
