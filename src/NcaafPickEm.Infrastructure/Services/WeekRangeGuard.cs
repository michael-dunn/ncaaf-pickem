using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// The one place every week-scoped game-set/point-rule route checks that <c>{week}</c> is a week
/// the league actually plays. A week outside the league's First..Last range does not exist for
/// this league, so callers 404 rather than 400 (05-Conventions.md, P3-03 orchestrator guidance).
/// </summary>
public static class WeekRangeGuard
{
    /// <summary>
    /// Loads the league and throws <see cref="GameSetRuleViolation"/>
    /// (<see cref="GameSetRuleViolationCode.WeekOutOfRange"/>) when <paramref name="week"/> is
    /// outside it. Returns the league so callers get <c>FirstWeek</c>/<c>LastWeek</c>/
    /// <c>DefaultPointValue</c>/<c>SeasonYear</c> without a second query.
    /// </summary>
    public static async Task<League> EnsureWeekInRangeAsync(
        AppDbContext database,
        Guid leagueId,
        int week,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        League league = await database.Leagues
            .AsNoTracking()
            .FirstAsync(candidate => candidate.Id == leagueId, cancellationToken)
            .ConfigureAwait(false);

        if (week < league.FirstWeek || week > league.LastWeek)
        {
            throw new GameSetRuleViolation(
                GameSetRuleViolationCode.WeekOutOfRange,
                $"Week {week} is outside this league's {league.FirstWeek}..{league.LastWeek} range.");
        }

        return league;
    }
}
