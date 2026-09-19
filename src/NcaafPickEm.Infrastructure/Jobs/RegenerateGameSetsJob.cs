using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Services;

namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// Tuesday 03:30 AM Eastern (Feature 02, P3-04): after the P2-04 rankings/schedule refresh jobs
/// have run (Sunday/Monday 20:00, Tuesday 03:00/03:10/03:20), regenerate every active league's
/// current and next unlocked week from its saved rules, so a newly ranked team's game or a
/// postponed game's removal shows up without the commissioner doing anything.
/// </summary>
/// <remarks>
/// Calls exactly the same <see cref="GameSetService.GetOrCreateWeekSetAsync"/> +
/// <see cref="GameSetService.GenerateAsync"/> pair the commissioner's "generate" button calls, so
/// a week with no <c>WeekGameSets</c> row yet gets one (satisfying "members always have a set"
/// for the current/next week without a separate auto-create step here - <see cref="EnsureCurrentWeekSetsJob"/>
/// still runs Sunday morning as the earlier backstop, and covers a league whose commissioner never
/// visits the config page before this job's Tuesday run).
/// <para>
/// A locked week is skipped quietly (logged at Information - happens every week for the week that
/// just locked). A week whose rules would exceed <see cref="GameSetLimits.MaxGames"/> is skipped
/// with a Warning log and does not stop the other weeks or leagues. Nothing here throws for an
/// individual league/week failure; only a truly unexpected exception (not a
/// <see cref="GameSetRuleViolation"/>) propagates, which fails the whole run - unlike the
/// two expected refusals above, that is not something this job knows how to recover from.
/// </para>
/// </remarks>
public sealed class RegenerateGameSetsJob : IScheduledJob
{
    private readonly AppDbContext _database;
    private readonly GameSetService _gameSetService;
    private readonly ISeasonWeekSource _seasonWeekSource;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RegenerateGameSetsJob> _logger;

    /// <summary>Creates the job.</summary>
    public RegenerateGameSetsJob(
        AppDbContext database,
        GameSetService gameSetService,
        ISeasonWeekSource seasonWeekSource,
        TimeProvider timeProvider,
        ILogger<RegenerateGameSetsJob> logger)
    {
        _database = database;
        _gameSetService = gameSetService;
        _seasonWeekSource = seasonWeekSource;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "RegenerateGameSets";

    /// <inheritdoc />
    public string CronExpression => "30 3 * * 2";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();

        List<League> leagues = await _database.Leagues
            .AsNoTracking()
            .Where(league => !league.IsComplete)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int leaguesProcessed = 0;
        int weeksRegenerated = 0;
        int weeksSkippedLocked = 0;
        int weeksSkippedExceedsMax = 0;

        foreach (League league in leagues)
        {
            IReadOnlyList<SeasonWeek> weeks = await _seasonWeekSource
                .GetWeeksAsync(league.SeasonYear, cancellationToken)
                .ConfigureAwait(false);

            if (weeks.Count == 0)
            {
                _logger.LogInformation(
                    "League {LeagueId}: no season calendar for {SeasonYear}; skipping.",
                    league.Id,
                    league.SeasonYear);
                continue;
            }

            CurrentWeek current = SeasonCalendar.CurrentWeekAt(nowUtc, weeks);
            int currentWeek = Math.Clamp(current.Week, league.FirstWeek, league.LastWeek);

            IReadOnlyList<int> targetWeeks = currentWeek + 1 <= league.LastWeek
                ? [currentWeek, currentWeek + 1]
                : [currentWeek];

            foreach (int week in targetWeeks)
            {
                try
                {
                    await _gameSetService.GetOrCreateWeekSetAsync(league.Id, week, cancellationToken).ConfigureAwait(false);
                    await _gameSetService.GenerateAsync(league.Id, week, cancellationToken).ConfigureAwait(false);
                    weeksRegenerated++;
                }
                catch (GameSetRuleViolation violation) when (violation.Code == GameSetRuleViolationCode.Locked)
                {
                    weeksSkippedLocked++;
                    _logger.LogInformation(
                        "League {LeagueId} week {Week} is already locked; skipping regeneration.",
                        league.Id,
                        week);
                }
                catch (GameSetRuleViolation violation) when (violation.Code == GameSetRuleViolationCode.ExceedsMax)
                {
                    weeksSkippedExceedsMax++;
                    _logger.LogWarning(
                        "League {LeagueId} week {Week}'s rules would select {Count} games, exceeding the maximum; skipping regeneration.",
                        league.Id,
                        week,
                        violation.Count);
                }
            }

            leaguesProcessed++;
        }

        _logger.LogInformation(
            "RegenerateGameSets occurrence {ScheduledFor:o}: {Leagues} leagues, {Regenerated} weeks regenerated, " +
            "{Locked} skipped (locked), {ExceedsMax} skipped (exceeds max).",
            scheduledFor,
            leaguesProcessed,
            weeksRegenerated,
            weeksSkippedLocked,
            weeksSkippedExceedsMax);
    }
}
