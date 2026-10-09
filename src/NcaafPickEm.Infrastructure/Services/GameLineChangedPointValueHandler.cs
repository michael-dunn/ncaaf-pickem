using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Seasons.Events;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Events;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Reacts to <see cref="GameLineChanged"/> (P3-06, D-189): re-resolves
/// <c>ResolvedPointValue</c> for the game in every league's set that is not yet frozen, so a
/// close-spread rule starts or stops matching as soon as the line crosses its threshold, rather
/// than at the next rules change or at lock.
/// </summary>
/// <remarks>
/// <see cref="PointValueRecalculator"/> does the resolving, against the same newest spread the
/// lock snapshot uses, and a manual override still wins there, so an overridden game keeps its
/// value. Frozen weeks are excluded by the query through
/// <see cref="WeekGameSetLockGuard.IsNotFrozen"/>, as in
/// <see cref="PointRuleService.ReResolveUnlockedWeeksAsync"/>: nothing after lock may change a
/// point value (04-Domain-Algorithms.md section 3). Idempotent: resolving twice against the same
/// line gives the same values.
/// </remarks>
public sealed class GameLineChangedPointValueHandler : IDomainEventHandler<GameLineChanged>
{
    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GameLineChangedPointValueHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public GameLineChangedPointValueHandler(
        AppDbContext database,
        TimeProvider timeProvider,
        ILogger<GameLineChangedPointValueHandler> logger)
    {
        _database = database;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(GameLineChanged domainEvent, CancellationToken cancellationToken)
    {
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        IQueryable<Guid> unfrozenSetIds = _database.WeekGameSets
            .Where(WeekGameSetLockGuard.IsNotFrozen(nowUtc))
            .Select(set => set.Id);

        List<WeekGameSetGame> rows = await _database.WeekGameSetGames
            .Include(row => row.WeekGameSet)
            .Include(row => row.Game!.HomeTeam)
            .Include(row => row.Game!.AwayTeam)
            .Where(row => row.GameId == domainEvent.GameId && !row.IsRemoved && !row.IsVoided)
            .Where(row => unfrozenSetIds.Contains(row.WeekGameSetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return;
        }

        Guid[] leagueIds = [.. rows.Select(row => row.WeekGameSet!.LeagueId).Distinct()];
        Dictionary<Guid, int> defaultPointValues = await _database.Leagues
            .AsNoTracking()
            .Where(league => leagueIds.Contains(league.Id))
            .ToDictionaryAsync(league => league.Id, league => league.DefaultPointValue, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, int> before = rows.ToDictionary(row => row.Id, row => row.ResolvedPointValue);

        foreach (IGrouping<Guid, WeekGameSetGame> league in rows.GroupBy(row => row.WeekGameSet!.LeagueId))
        {
            await PointValueRecalculator
                .RecalculateAsync(_database, league.Key, defaultPointValues[league.Key], [.. league], cancellationToken)
                .ConfigureAwait(false);
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        int changed = rows.Count(row => row.ResolvedPointValue != before[row.Id]);
        _logger.LogInformation(
            "Line change on game {GameId}: re-resolved {Rows} point values in {Leagues} leagues' open weeks, {Changed} changed.",
            domainEvent.GameId,
            rows.Count,
            leagueIds.Length,
            changed);
    }
}
