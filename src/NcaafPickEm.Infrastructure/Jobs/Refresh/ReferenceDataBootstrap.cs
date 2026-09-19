using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Services;

namespace NcaafPickEm.Infrastructure.Jobs.Refresh;

/// <summary>
/// The one-time "make a brand-new database usable" ingest (P8-06, D-164): calendar, teams, then
/// the current week's schedule, rankings and lines.
/// </summary>
/// <remarks>
/// <para>
/// Under <c>Providers:ReferenceData=Cfbd</c> every one of those tables is filled by the refresh
/// jobs in this folder, and the earliest of them is the Tuesday 03:10 ET schedule refresh. On a
/// fresh deployment that leaves the app with no <c>SeasonWeeks</c> until the following Tuesday,
/// which makes <c>GET /api/seasons/{year}/weeks</c> empty, league creation fall back to a default
/// week range, and a new league unable to generate a week's games - so the very first thing an
/// operator does after starting the stack is the thing that works least well. This runs the same
/// ingest methods the cron jobs run, once, right after startup.
/// </para>
/// <para>
/// This is the work only; <see cref="ReferenceDataBootstrapHostedService"/> owns when it runs and
/// whether it runs at all. Every step goes through <see cref="ReferenceDataIngestService"/>, so
/// each one records <c>DataRefreshStatus</c>, never throws on a provider failure, and is
/// idempotent - a cron job ticking while this is still running simply upserts the same rows.
/// </para>
/// </remarks>
public sealed class ReferenceDataBootstrap
{
    /// <summary>Provider calls <see cref="ReferenceDataIngestService.IngestTeamsAsync"/> makes: conferences, then teams.</summary>
    private const int TeamsProviderCalls = 2;

    private readonly AppDbContext _database;
    private readonly ReferenceDataIngestService _ingest;
    private readonly ISeasonWeekSource _weekSource;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReferenceDataBootstrap> _logger;

    /// <summary>Creates the bootstrap.</summary>
    public ReferenceDataBootstrap(
        AppDbContext database,
        ReferenceDataIngestService ingest,
        ISeasonWeekSource weekSource,
        TimeProvider timeProvider,
        ILogger<ReferenceDataBootstrap> logger)
    {
        _database = database;
        _ingest = ingest;
        _weekSource = weekSource;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Ingests the season's reference data when the database does not have it yet, and reports
    /// what it did. Provider failures are reported, never thrown: the cron jobs retry on their
    /// own schedules.
    /// </summary>
    public async Task<ReferenceDataBootstrapResult> RunAsync(CancellationToken cancellationToken = default)
    {
        int season = RefreshJobSupport.CurrentSeasonYear(_timeProvider.GetUtcNow());

        // Deliberately the tables, not ISeasonWeekSource: DbSeasonWeekSource caches per season
        // process-wide, and "has this database ever been filled?" must be answered by the
        // database itself.
        bool hasCalendar = await _database.SeasonWeeks
            .AnyAsync(week => week.SeasonYear == season, cancellationToken)
            .ConfigureAwait(false);
        bool hasTeams = await _database.Teams.AnyAsync(cancellationToken).ConfigureAwait(false);

        if (hasCalendar && hasTeams)
        {
            _logger.LogInformation(
                "Reference data bootstrap for {Season}: nothing to do, the calendar and teams are already on file",
                season);
            return ReferenceDataBootstrapResult.NotNeeded(season);
        }

        _logger.LogInformation(
            "Reference data bootstrap for {Season} starting (calendar on file: {HasCalendar}, teams on file: {HasTeams})",
            season,
            hasCalendar,
            hasTeams);

        var run = new Run(season);

        CalendarIngestResult calendar = await _ingest.IngestCalendarAsync(season, cancellationToken)
            .ConfigureAwait(false);
        run.Record(calendar.Success, calendar.Error, "calendar", week: null);

        TeamsIngestResult teams = await _ingest.IngestTeamsAsync(season, cancellationToken).ConfigureAwait(false);
        run.Record(teams.Success, teams.Error, "teams", week: null, TeamsProviderCalls);

        int? currentWeek = await ResolveCurrentWeekAsync(season, cancellationToken).ConfigureAwait(false);
        if (currentWeek is int week)
        {
            await IngestWeekAsync(season, week, run, cancellationToken).ConfigureAwait(false);
        }

        LogSummary(run, currentWeek);

        return run.ToResult(currentWeek);
    }

    /// <summary>
    /// The week to fetch: the calendar's own current week, clamped into the regular season so a
    /// bootstrap run in December does not fetch championship week instead.
    /// </summary>
    private async Task<int?> ResolveCurrentWeekAsync(int season, CancellationToken cancellationToken)
    {
        IReadOnlyList<SeasonWeek> weeks = await _weekSource.GetWeeksAsync(season, cancellationToken)
            .ConfigureAwait(false);

        if (weeks.Count == 0 || !weeks.Any(week => week.IsRegularSeason))
        {
            return null;
        }

        int currentWeek = SeasonCalendar.CurrentWeekAt(_timeProvider.GetUtcNow(), weeks).Week;
        return SeasonCalendar.ClampToRegularSeason(currentWeek, weeks);
    }

    /// <summary>
    /// The current week's slices, in the order the cron jobs would have produced them: the
    /// schedule for this week and the next (as <see cref="ScheduleRefreshRunner"/> does, so a
    /// league created today can already see next week), then rankings, then lines.
    /// </summary>
    private async Task IngestWeekAsync(int season, int week, Run run, CancellationToken cancellationToken)
    {
        ScheduleIngestResult schedule = await _ingest.IngestScheduleAsync(season, week, cancellationToken)
            .ConfigureAwait(false);
        run.Record(schedule.Success, schedule.Error, "schedule", week);

        int? lastRegularWeek = await RefreshJobSupport
            .LastRegularSeasonWeekAsync(_weekSource, season, cancellationToken)
            .ConfigureAwait(false);

        if (lastRegularWeek is int lastWeek && week < lastWeek)
        {
            ScheduleIngestResult nextWeek = await _ingest
                .IngestScheduleAsync(season, week + 1, cancellationToken)
                .ConfigureAwait(false);
            run.Record(nextWeek.Success, nextWeek.Error, "schedule", week + 1);
        }

        RankingsIngestResult rankings = await _ingest.IngestRankingsAsync(season, week, cancellationToken)
            .ConfigureAwait(false);
        run.Record(rankings.Success, rankings.Error, "rankings", week);

        LinesIngestResult lines = await _ingest.IngestLinesAsync(season, week, cancellationToken)
            .ConfigureAwait(false);
        run.Record(lines.Success, lines.Error, "lines", week);
    }

    private void LogSummary(Run run, int? currentWeek)
    {
        if (currentWeek is null)
        {
            _logger.LogWarning(
                "Reference data bootstrap for {Season} stopped after {Steps} step(s): no season weeks on file, "
                + "so there is no current week to fetch. The refresh jobs will retry on their schedules. {Errors}",
                run.Season,
                run.Steps,
                string.Join("; ", run.Errors));
            return;
        }

        if (run.Failures > 0)
        {
            _logger.LogWarning(
                "Reference data bootstrap for {Season} week {Week}: {Steps} ingest step(s), {Failures} failed, "
                + "about {ProviderCalls} provider call(s). The refresh jobs will retry on their schedules. {Errors}",
                run.Season,
                currentWeek,
                run.Steps,
                run.Failures,
                run.ProviderCalls,
                string.Join("; ", run.Errors));
            return;
        }

        _logger.LogInformation(
            "Reference data bootstrap for {Season} week {Week} finished: {Steps} ingest step(s), "
            + "about {ProviderCalls} provider call(s)",
            run.Season,
            currentWeek,
            run.Steps,
            run.ProviderCalls);
    }

    /// <summary>Running totals behind the one summary line a bootstrap logs.</summary>
    private sealed class Run(int season)
    {
        public int Season { get; } = season;

        public int Steps { get; private set; }

        public int Failures { get; private set; }

        public int ProviderCalls { get; private set; }

        public List<string> Errors { get; } = [];

        public void Record(bool success, string? error, string slice, int? week, int providerCalls = 1)
        {
            Steps++;
            ProviderCalls += providerCalls;

            if (success)
            {
                return;
            }

            Failures++;

            // The ingest already recorded this against DataRefreshStatus, which the data status
            // page reads; collecting it here keeps the bootstrap itself to one log line.
            Errors.Add(week is int weekNumber ? $"{slice} week {weekNumber}: {error}" : $"{slice}: {error}");
        }

        public ReferenceDataBootstrapResult ToResult(int? currentWeek) =>
            new(true, Season, currentWeek, Steps, Failures, ProviderCalls, [.. Errors]);
    }
}

/// <summary>
/// What one <see cref="ReferenceDataBootstrap.RunAsync"/> did.
/// </summary>
/// <param name="Ran">False when the database already had the season's reference data.</param>
/// <param name="Season">The season the run was for.</param>
/// <param name="CurrentWeek">The week fetched, or null when no calendar could be resolved.</param>
/// <param name="Steps">How many ingest methods were called.</param>
/// <param name="Failures">How many of them reported a failure.</param>
/// <param name="ProviderCalls">Roughly how many provider calls those steps made.</param>
/// <param name="Errors">One message per failed step.</param>
public sealed record ReferenceDataBootstrapResult(
    bool Ran,
    int Season,
    int? CurrentWeek,
    int Steps,
    int Failures,
    int ProviderCalls,
    IReadOnlyList<string> Errors)
{
    /// <summary>The result for a database that already had the season's reference data.</summary>
    public static ReferenceDataBootstrapResult NotNeeded(int season) => new(false, season, null, 0, 0, 0, []);
}
