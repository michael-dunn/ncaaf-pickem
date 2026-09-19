using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Services;

namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// Sunday 00:05 AM Eastern (Feature 02, P3-04), right after the week rolls over
/// (13-Season-Calendar.txt): for every active league whose current week has no
/// <c>WeekGameSets</c> row yet, create and generate one from the league's default rules so no
/// member ever opens the app to an empty week just because the commissioner has not visited the
/// config page.
/// </summary>
/// <remarks>
/// This is the early backstop; <see cref="RegenerateGameSetsJob"/>'s own
/// <see cref="GameSetService.GetOrCreateWeekSetAsync"/> call covers the same case again three
/// days later for whatever this job missed (a league created between Sunday 00:05 and Tuesday
/// 03:30, say). A league whose current week already has a row - generated, overridden, or even
/// empty - is left alone; this job only ever fills a genuinely missing row, never re-generates
/// one that exists (that is <see cref="RegenerateGameSetsJob"/>'s job, on its own schedule).
/// </remarks>
public sealed class EnsureCurrentWeekSetsJob : IScheduledJob
{
    private readonly AppDbContext _database;
    private readonly GameSetService _gameSetService;
    private readonly ISeasonWeekSource _seasonWeekSource;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EnsureCurrentWeekSetsJob> _logger;

    /// <summary>Creates the job.</summary>
    public EnsureCurrentWeekSetsJob(
        AppDbContext database,
        GameSetService gameSetService,
        ISeasonWeekSource seasonWeekSource,
        TimeProvider timeProvider,
        ILogger<EnsureCurrentWeekSetsJob> logger)
    {
        _database = database;
        _gameSetService = gameSetService;
        _seasonWeekSource = seasonWeekSource;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "EnsureCurrentWeekSets";

    /// <inheritdoc />
    public string CronExpression => "5 0 * * 0";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();

        List<League> leagues = await _database.Leagues
            .AsNoTracking()
            .Where(league => !league.IsComplete)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int created = 0;

        foreach (League league in leagues)
        {
            IReadOnlyList<SeasonWeek> weeks = await _seasonWeekSource
                .GetWeeksAsync(league.SeasonYear, cancellationToken)
                .ConfigureAwait(false);

            if (weeks.Count == 0)
            {
                continue;
            }

            int week = SeasonCalendar.CurrentWeekAt(nowUtc, weeks).Week;

            if (week < league.FirstWeek || week > league.LastWeek)
            {
                continue;
            }

            bool exists = await _database.WeekGameSets
                .AsNoTracking()
                .AnyAsync(set => set.LeagueId == league.Id && set.Week == week, cancellationToken)
                .ConfigureAwait(false);

            if (exists)
            {
                continue;
            }

            try
            {
                await _gameSetService.GetOrCreateWeekSetAsync(league.Id, week, cancellationToken).ConfigureAwait(false);
                await _gameSetService.GenerateAsync(league.Id, week, cancellationToken).ConfigureAwait(false);
                created++;
                _logger.LogInformation(
                    "Auto-created league {LeagueId} week {Week}'s game set from the default rules.",
                    league.Id,
                    week);
            }
            catch (GameSetRuleViolation violation) when (violation.Code == GameSetRuleViolationCode.ExceedsMax)
            {
                // The (now-created, empty) WeekGameSets row stands; the commissioner sees an
                // empty week and can fix the rules from the config page rather than the job
                // silently picking 50 of however many the rules matched.
                _logger.LogWarning(
                    "League {LeagueId} week {Week}'s default rules would select {Count} games, exceeding the maximum; left empty.",
                    league.Id,
                    week,
                    violation.Count);
            }
        }

        _logger.LogInformation(
            "EnsureCurrentWeekSets occurrence {ScheduledFor:o}: {Created} week(s) auto-created.",
            scheduledFor,
            created);
    }
}
