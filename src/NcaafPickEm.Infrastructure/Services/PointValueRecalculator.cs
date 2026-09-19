using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Points;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Recomputes <c>WeekGameSetGames.ResolvedPointValue</c> for a set of rows through
/// <see cref="PointValueResolver"/>. Shared by <see cref="GameSetService"/> (after generate/manual
/// add) and <see cref="PointRuleService"/> (after a rule/default/override change), so the two
/// never drift on what "current spread" or "the league's rules" means.
/// </summary>
public static class PointValueRecalculator
{
    /// <summary>
    /// Recomputes <see cref="WeekGameSetGame.ResolvedPointValue"/> for every row in
    /// <paramref name="rows"/>. Rows must have <c>Game</c>, <c>Game.HomeTeam</c>, and
    /// <c>Game.AwayTeam</c> loaded. Does not save.
    /// </summary>
    /// <param name="database">The database, used to load the league's point rules and the
    /// latest spread for each row's game.</param>
    /// <param name="leagueId">The league the rows belong to.</param>
    /// <param name="leagueDefaultPointValue">The league's default point value.</param>
    /// <param name="rows">The rows to recompute. Removed and voided rows are skipped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task RecalculateAsync(
        AppDbContext database,
        Guid leagueId,
        int leagueDefaultPointValue,
        IReadOnlyList<WeekGameSetGame> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(rows);

        List<WeekGameSetGame> active = [.. rows.Where(row => !row.IsRemoved && !row.IsVoided)];
        if (active.Count == 0)
        {
            return;
        }

        IReadOnlyList<PointRuleInfo> pointRules = await LoadPointRuleInfosAsync(database, leagueId, cancellationToken)
            .ConfigureAwait(false);

        Guid[] gameIds = [.. active.Select(row => row.GameId).Distinct()];

        Dictionary<Guid, decimal> currentSpreads = await LoadLatestSpreadsAsync(database, gameIds, cancellationToken)
            .ConfigureAwait(false);

        foreach (WeekGameSetGame row in active)
        {
            Game game = row.Game
                ?? throw new InvalidOperationException(
                    $"WeekGameSetGame {row.Id} was passed to RecalculateAsync without its Game loaded.");

            var gameInfo = new PointGameInfo(
                game.HomeTeamId,
                game.AwayTeamId,
                game.HomeTeam?.ConferenceId,
                game.AwayTeam?.ConferenceId,
                game.IsConferenceGame);

            decimal? spread = currentSpreads.TryGetValue(row.GameId, out decimal value) ? value : null;

            PointResolution resolution = PointValueResolver.Resolve(
                gameInfo,
                row.PointValueOverride,
                leagueDefaultPointValue,
                pointRules,
                spread);

            row.ResolvedPointValue = resolution.Value;
        }
    }

    /// <summary>
    /// The newest <c>GameLines.Spread</c> for each of <paramref name="gameIds"/>, by
    /// <c>FetchedUtc</c>. Games with no line are absent from the dictionary.
    /// </summary>
    /// <param name="database">The context.</param>
    /// <param name="gameIds">The games to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// "The current spread" has to mean one thing everywhere: this feeds both the ordinary
    /// recalculation above and P4-02's <c>SpreadAtLock</c> snapshot, so the value frozen at lock
    /// is the same one the week was showing a second earlier.
    /// </remarks>
    public static async Task<Dictionary<Guid, decimal>> LoadLatestSpreadsAsync(
        AppDbContext database,
        IReadOnlyCollection<Guid> gameIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(gameIds);

        if (gameIds.Count == 0)
        {
            return [];
        }

        List<GameLine> lines = await database.GameLines
            .AsNoTracking()
            .Where(line => gameIds.Contains(line.GameId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return lines
            .GroupBy(line => line.GameId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(line => line.FetchedUtc).First().Spread);
    }

    /// <summary>Loads the league's point rules, flattened for <see cref="PointValueResolver"/>.</summary>
    public static async Task<IReadOnlyList<PointRuleInfo>> LoadPointRuleInfosAsync(
        AppDbContext database,
        Guid leagueId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        return await database.PointRules
            .AsNoTracking()
            .Where(rule => rule.LeagueId == leagueId)
            .Select(rule => new PointRuleInfo(
                rule.Priority,
                rule.RuleType,
                rule.ConferenceId,
                rule.TeamId,
                rule.SpreadThreshold,
                rule.PointValue,
                rule.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
