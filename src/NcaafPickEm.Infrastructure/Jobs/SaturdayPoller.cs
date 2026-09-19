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
/// Disabled entirely when <c>Jobs:Enabled</c> is false, same as <see cref="JobScheduler"/>. Every
/// poll — in or out of the window — is a single re-evaluation once a minute; there is no separate
/// per-Saturday timer to schedule or cancel.
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
                await EvaluateOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
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

        if (!decision.InWindow)
        {
            _lastWindowOpenedForDate = null;
            return;
        }

        DateOnly easternDate = DateOnly.FromDateTime(SeasonCalendar.ToEastern(nowUtc).Date);
        if (_lastWindowOpenedForDate != easternDate)
        {
            _health.ResetForNewDay();
            _lastWindowOpenedForDate = easternDate;
            _logger.LogInformation("Saturday poller window opened for {EasternDate}", easternDate);
        }

        await PollOnceAsync(scope.ServiceProvider, database, _timeProvider, easternDate, _logger, cancellationToken);
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

        DataRefreshStatus? status = await database.DataRefreshStatuses
            .FirstOrDefaultAsync(row => row.DataType == RefreshDataType.Scores, cancellationToken)
            .ConfigureAwait(false);

        if (status is null)
        {
            status = new DataRefreshStatus { DataType = RefreshDataType.Scores };
            database.DataRefreshStatuses.Add(status);
        }

        status.LastAttemptUtc = nowUtc;
        status.LastSuccessUtc = nowUtc;
        status.LastError = null;

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

            int currentWeek = SeasonCalendar.CurrentWeekAt(nowUtc, weeks).Week;
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

    private static bool IsTerminal(GameStatus status) =>
        status is GameStatus.Final or GameStatus.Postponed or GameStatus.Cancelled;
}
