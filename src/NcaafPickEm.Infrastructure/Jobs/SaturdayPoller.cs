using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// Polls live scores on Saturdays, per 04-Domain-Algorithms.md section 10. The window, cadence
/// and fallback decision are <see cref="SaturdayPollerSchedule"/>, a pure class this only wires up
/// to a clock, a database and <see cref="ILiveScoreProvider"/>.
/// </summary>
/// <remarks>
/// Disabled entirely when <c>Jobs:Enabled</c> is false, same as <see cref="JobScheduler"/>. The
/// loop re-evaluates the window once a minute — there is no separate per-Saturday timer to
/// schedule or cancel — but the provider is only called once per <see cref="PollerCadence"/>
/// interval while the window is open, never on every tick.
/// </remarks>
public sealed class SaturdayPoller : BackgroundService
{
    /// <summary>How often the poller re-evaluates whether it is time to call the provider.</summary>
    public static readonly TimeSpan EvaluationInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILiveScoreHealth _health;
    private readonly JobsOptions _options;
    private readonly ILogger<SaturdayPoller> _logger;

    private DateOnly? _lastWindowOpenedForDate;
    private DateTimeOffset? _nextPollDueUtc;

    /// <summary>Creates the poller.</summary>
    public SaturdayPoller(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILiveScoreHealth health,
        IOptions<JobsOptions> options,
        ILogger<SaturdayPoller> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _health = health;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Saturday poller is off (Jobs:Enabled = false); it will never call a live-score provider");
            return;
        }

        await Task.Yield();

        using var timer = new PeriodicTimer(EvaluationInterval, _timeProvider);

        try
        {
            do
            {
                await EvaluateGuardedAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// One guarded evaluation. A provider outage, a database blip or a bad payload must never
    /// end the loop for the lifetime of the process — same contract as
    /// <see cref="JobScheduler"/>'s tick — because a dead <see cref="BackgroundService"/> means
    /// no live scores for the rest of the Saturday (and, with the default
    /// <c>BackgroundServiceExceptionBehavior</c>, a stopped host).
    /// </summary>
    private async Task EvaluateGuardedAsync(CancellationToken stoppingToken)
    {
        try
        {
            await EvaluateOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Saturday poller evaluation failed; retrying on the next tick");
        }
    }

    /// <summary>
    /// One re-evaluation: builds the active sets, asks <see cref="SaturdayPollerSchedule"/>
    /// whether now is in the window, and polls once if so. Public so a test (or a manual
    /// <c>POST /api/admin/refresh/Scores</c>) can drive exactly one pass without waiting on the
    /// timer.
    /// </summary>
    public async Task EvaluateOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ISeasonWeekSource weekSource = scope.ServiceProvider.GetRequiredService<ISeasonWeekSource>();

        IReadOnlyList<PollerWeekSet> activeSets = await LoadActiveSetsAsync(database, weekSource, nowUtc, cancellationToken);
        PollerDecision decision = SaturdayPollerSchedule.Evaluate(nowUtc, activeSets, _health.ActiveSource);

        if (!decision.InWindow || decision.SaturdayEastern is not DateOnly easternDate)
        {
            _lastWindowOpenedForDate = null;
            _nextPollDueUtc = null;
            return;
        }

        // Keyed on the window's Saturday, not on "today": a tick at 00:30 ET Sunday is still the
        // same window, and resetting the health there would clear a fallback that engaged hours
        // earlier and is meant to hold for the rest of the game day.
        if (_lastWindowOpenedForDate != easternDate)
        {
            _health.ResetForNewDay();
            _lastWindowOpenedForDate = easternDate;
            _nextPollDueUtc = null;
            _logger.LogInformation("Saturday poller window opened for {EasternDate}", easternDate);
        }

        // The window is re-evaluated every minute; the provider is only called on the cadence the
        // active source earns (5 min ESPN, 10 min CFBD - 04-Domain-Algorithms.md section 10).
        if (_nextPollDueUtc is DateTimeOffset dueUtc && nowUtc < dueUtc)
        {
            return;
        }

        // Advanced before the call, not after, so a failing provider is retried on the next
        // cadence tick rather than once a minute - which would burn a month of CFBD's free tier
        // in a single Saturday.
        _nextPollDueUtc = decision.NextPollAtUtc;

        try
        {
            await PollOnceAsync(scope.ServiceProvider, database, _timeProvider, easternDate, _logger, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordRefreshFailureAsync(database, _timeProvider, exception.Message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Fetches one snapshot for <paramref name="easternDate"/> and applies it. Static so both the
    /// timer loop and a manual "refresh scores now" admin call go through the exact same path on
    /// their own scope. Returns the apply result so a test can assert on matches and events
    /// without subscribing.
    /// </summary>
    public static async Task<LiveScoreApplyResult> PollOnceAsync(
        IServiceProvider services,
        AppDbContext database,
        TimeProvider timeProvider,
        DateOnly easternDate,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ILiveScoreProvider provider = services.GetRequiredService<ILiveScoreProvider>();
        LiveScoreApplyService applyService = services.GetRequiredService<LiveScoreApplyService>();

        IReadOnlyList<LiveScoreUpdate> updates = await provider
            .GetScoresAsync(easternDate, cancellationToken)
            .ConfigureAwait(false);

        LiveScoreApplyResult result = await applyService
            .ApplyAsync(easternDate, updates, cancellationToken)
            .ConfigureAwait(false);

        await RecordRefreshStatusAsync(database, timeProvider, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Saturday poll for {EasternDate}: {Fetched} fetched, {Matched} matched, {Changed} changed, " +
            "{Unmatched} unmatched, {Ignored} ignored, {EventCount} events",
            easternDate,
            updates.Count,
            result.Matched,
            result.Changed,
            result.Unmatched,
            result.Ignored,
            result.Events.Count);

        return result;
    }

    private static async Task RecordRefreshStatusAsync(
        AppDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        DataRefreshStatus status = await GetOrAddScoresStatusAsync(database, cancellationToken).ConfigureAwait(false);

        status.LastAttemptUtc = nowUtc;
        status.LastSuccessUtc = nowUtc;
        status.LastError = null;

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a failed poll on <c>DataRefreshStatus(Scores)</c> so the data status page can show
    /// it, best-effort: if the database is what failed, the caller's own error is the one that
    /// matters and this must not mask it.
    /// </summary>
    /// <remarks>
    /// The change tracker is cleared first: the failure may have happened part way through
    /// <see cref="LiveScoreApplyService.ApplyAsync"/>, and saving here must not commit a
    /// half-applied snapshot (the same ordering <c>ReferenceDataIngestService</c> relies on).
    /// </remarks>
    private static async Task RecordRefreshFailureAsync(
        AppDbContext database,
        TimeProvider timeProvider,
        string error,
        CancellationToken cancellationToken)
    {
        try
        {
            database.ChangeTracker.Clear();

            DataRefreshStatus status = await GetOrAddScoresStatusAsync(database, cancellationToken).ConfigureAwait(false);

            status.LastAttemptUtc = timeProvider.GetUtcNow().UtcDateTime;
            status.LastError = error;

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Swallowed on purpose; the original exception is rethrown by the caller and logged.
        }
    }

    private static async Task<DataRefreshStatus> GetOrAddScoresStatusAsync(
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        DataRefreshStatus? status = await database.DataRefreshStatuses
            .FirstOrDefaultAsync(row => row.DataType == RefreshDataType.Scores, cancellationToken)
            .ConfigureAwait(false);

        if (status is null)
        {
            status = new DataRefreshStatus { DataType = RefreshDataType.Scores };
            database.DataRefreshStatuses.Add(status);
        }

        return status;
    }

    /// <summary>
    /// Every league's current-week set, flattened to what <see cref="SaturdayPollerSchedule"/>
    /// needs. Leagues are grouped by season so each season's calendar is read once.
    /// </summary>
    private static async Task<IReadOnlyList<PollerWeekSet>> LoadActiveSetsAsync(
        AppDbContext database,
        ISeasonWeekSource weekSource,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        List<League> leagues = await database.Leagues
            .AsNoTracking()
            .Where(league => !league.IsComplete)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (leagues.Count == 0)
        {
            return [];
        }

        List<PollerWeekSet> sets = [];

        foreach (IGrouping<int, League> bySeasonYear in leagues.GroupBy(league => league.SeasonYear))
        {
            IReadOnlyList<SeasonWeek> weeks = await weekSource
                .GetWeeksAsync(bySeasonYear.Key, cancellationToken)
                .ConfigureAwait(false);

            if (weeks.Count == 0)
            {
                continue;
            }

            int currentWeek = SeasonCalendar.CurrentWeekAt(GameDayInstant(nowUtc), weeks).Week;
            Guid[] leagueIds = [.. bySeasonYear.Select(league => league.Id)];

            var rows = await database.WeekGameSetGames
                .AsNoTracking()
                .Where(game => !game.IsRemoved
                    && leagueIds.Contains(game.WeekGameSet!.LeagueId)
                    && game.WeekGameSet.Week == currentWeek)
                .Select(game => new
                {
                    game.WeekGameSet!.LeagueId,
                    game.WeekGameSet.LockAtUtc,
                    game.IsVoided,
                    Status = game.Game!.Status,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            IEnumerable<Guid> leaguesWithSets = rows.Select(row => row.LeagueId).Distinct();
            foreach (Guid leagueId in leaguesWithSets)
            {
                var byLeague = rows.Where(row => row.LeagueId == leagueId).ToList();
                DateTime? lockAtUtc = byLeague[0].LockAtUtc;
                bool allTerminal = byLeague.All(row => row.IsVoided || IsTerminal(row.Status));
                sets.Add(new PollerWeekSet(leagueId, lockAtUtc, allTerminal));
            }
        }

        return sets;
    }

    /// <summary>
    /// The instant to resolve "which week is the poller working" at. A season week ends Saturday
    /// 23:59:59.999 ET (04-Domain-Algorithms.md section 1), but the poller's window runs to 03:00
    /// ET on Sunday: between those two the calendar has already rolled to the next week while the
    /// late game is still playing. Asking the calendar about Saturday evening instead keeps that
    /// game's set loaded until the window closes for real.
    /// </summary>
    /// <remarks>
    /// Shifting the Eastern calendar date, not the UTC instant, is what makes this right on the
    /// November DST Sunday, where 02:00 ET happens twice and a fixed three-hour subtraction lands
    /// on the wrong side of midnight.
    /// </remarks>
    private static DateTimeOffset GameDayInstant(DateTimeOffset nowUtc)
    {
        DateTimeOffset eastern = SeasonCalendar.ToEastern(nowUtc);

        if (eastern.DayOfWeek != DayOfWeek.Sunday
            || eastern.Hour >= SaturdayPollerSchedule.HardCloseHourEastern)
        {
            return nowUtc;
        }

        DateOnly saturday = DateOnly.FromDateTime(eastern.Date).AddDays(-1);
        return SeasonCalendar.ToUtc(saturday.ToDateTime(new TimeOnly(23, 0)));
    }

    private static bool IsTerminal(GameStatus status) =>
        status is GameStatus.Final or GameStatus.Postponed or GameStatus.Cancelled;
}
