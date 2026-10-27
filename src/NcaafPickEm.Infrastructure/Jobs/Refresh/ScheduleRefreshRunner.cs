using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Services;

namespace NcaafPickEm.Infrastructure.Jobs.Refresh;

/// <summary>
/// The work behind both <see cref="ScheduleRefreshJob"/> and <see cref="ScheduleRefreshDailyJob"/>:
/// refresh the calendar, then the schedule for the previous, current and next week, so a Tuesday
/// postponement discovered mid-week, next week's slate and last week's finals are all current
/// (Feature 09, 13, 17).
/// </summary>
/// <remarks>
/// <c>CalendarRefreshJob</c> is folded in here rather than being its own registration: the
/// calendar (<c>SeasonWeeks</c>) and the schedule (<c>Games</c>) are both schedule-domain data and
/// <see cref="ReferenceDataIngestService.IngestCalendarAsync"/> already reports under
/// <c>RefreshDataType.Schedule</c> (D-057).
/// </remarks>
public sealed class ScheduleRefreshRunner
{
    private readonly ReferenceDataIngestService _ingest;
    private readonly ISeasonWeekSource _weekSource;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScheduleRefreshRunner> _logger;

    /// <summary>Creates the runner.</summary>
    public ScheduleRefreshRunner(
        ReferenceDataIngestService ingest,
        ISeasonWeekSource weekSource,
        TimeProvider timeProvider,
        ILogger<ScheduleRefreshRunner> logger)
    {
        _ingest = ingest;
        _weekSource = weekSource;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Runs the calendar-then-schedule refresh for whichever season is current now.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        int season = RefreshJobSupport.CurrentSeasonYear(nowUtc);

        CalendarIngestResult calendar = await _ingest.IngestCalendarAsync(season, cancellationToken).ConfigureAwait(false);
        if (!calendar.Success)
        {
            _logger.LogWarning("Calendar refresh for {Season} failed: {Error}", season, calendar.Error);
        }
        else
        {
            _logger.LogInformation("Calendar refresh for {Season}: {Weeks} weeks", season, calendar.Weeks);
        }

        int? currentWeek = await RefreshJobSupport
            .CurrentWeekAsync(_weekSource, season, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (currentWeek is not int week)
        {
            _logger.LogWarning("Schedule refresh for {Season} skipped: no calendar on file yet", season);
            return;
        }

        int? lastRegularWeek = await RefreshJobSupport
            .LastRegularSeasonWeekAsync(_weekSource, season, cancellationToken)
            .ConfigureAwait(false);

        // The previous week too, so a game the live feed never finalized still turns Final from
        // CFBD's Completed flag and reaches the recent form tray (Feature 17, D-194). First, so a
        // failure here is not the last word on the refresh status when this week succeeds.
        if (week > 1)
        {
            await IngestWeekAsync(season, week - 1, cancellationToken).ConfigureAwait(false);
        }

        await IngestWeekAsync(season, week, cancellationToken).ConfigureAwait(false);

        if (lastRegularWeek is int lastWeek && week < lastWeek)
        {
            await IngestWeekAsync(season, week + 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task IngestWeekAsync(int season, int week, CancellationToken cancellationToken)
    {
        ScheduleIngestResult result = await _ingest
            .IngestScheduleAsync(season, week, cancellationToken)
            .ConfigureAwait(false);

        if (result.Success)
        {
            _logger.LogInformation(
                "Schedule refresh for {Season} week {Week}: {Upserted} upserted, {Postponed} postponed, {Restored} restored",
                season,
                week,
                result.Upserted,
                result.Postponed,
                result.Restored);
        }
        else
        {
            _logger.LogWarning("Schedule refresh for {Season} week {Week} failed: {Error}", season, week, result.Error);
        }
    }
}
