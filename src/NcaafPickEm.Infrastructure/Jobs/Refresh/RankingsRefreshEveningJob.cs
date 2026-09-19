namespace NcaafPickEm.Infrastructure.Jobs.Refresh;

/// <summary>
/// The AP poll usually drops Sunday afternoon; Monday evening is the safety-net re-fetch in case
/// it changes or was late (Feature 09). Cronos accepts a day-of-week list, so both occurrences are
/// one cron expression.
/// </summary>
public sealed class RankingsRefreshEveningJob : IScheduledJob
{
    private readonly RankingsRefreshRunner _runner;

    /// <summary>Creates the job.</summary>
    public RankingsRefreshEveningJob(RankingsRefreshRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name => "RankingsRefreshEvening";

    /// <inheritdoc />
    public string CronExpression => "0 20 * * 0,1";

    /// <inheritdoc />
    public Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken) =>
        _runner.RunAsync(cancellationToken);
}
