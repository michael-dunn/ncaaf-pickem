using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Points;

/// <summary>
/// Decides what one game in one league week is worth (Feature 03,
/// <c>04-Domain-Algorithms.md</c> section 3). Pure: no clock, no I/O, no entities, no mutation.
/// </summary>
/// <remarks>
/// The order is fixed: a commissioner's manual override wins over everything, then the first
/// matching rule in priority order, then the league default. Callers are responsible for only
/// resolving unlocked weeks; nothing here knows about lock.
/// </remarks>
public static class PointValueResolver
{
    /// <summary>
    /// Resolves the point value of one game.
    /// </summary>
    /// <param name="game">The game's matchup facts.</param>
    /// <param name="pointValueOverride">The commissioner's manual value for this game this week, or
    /// null when the rules decide.</param>
    /// <param name="leagueDefault">The league's default point value, used when nothing matches.</param>
    /// <param name="rules">The league's point rules. Sorted by priority here, so an unsorted list
    /// still resolves correctly; equal priorities keep their input order.</param>
    /// <param name="currentSpread">The current line for the game (home minus away), or null when no
    /// line is available. A missing spread never matches a close-spread rule.</param>
    /// <returns>The value and where it came from.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public static PointResolution Resolve(
        PointGameInfo game,
        int? pointValueOverride,
        int leagueDefault,
        IReadOnlyList<PointRuleInfo> rules,
        decimal? currentSpread)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (pointValueOverride is { } manualValue)
        {
            return new PointResolution(manualValue, PointValueSource.Override, null);
        }

        foreach (var rule in InPriorityOrder(rules))
        {
            if (Matches(rule, game, currentSpread))
            {
                return new PointResolution(rule.PointValue, PointValueSource.Rule, rule.RuleId);
            }
        }

        return new PointResolution(leagueDefault, PointValueSource.Default, null);
    }

    /// <summary>
    /// Whether a resolved value should be emphasized on the picks page. Equal to the default is not
    /// elevated; only strictly more is.
    /// </summary>
    /// <param name="resolvedPointValue">The value <see cref="Resolve"/> produced.</param>
    /// <param name="leagueDefault">The league's default point value.</param>
    /// <returns>Whether the game is worth more than the league default.</returns>
    public static bool IsElevated(int resolvedPointValue, int leagueDefault) =>
        resolvedPointValue > leagueDefault;

    /// <summary>
    /// Whether one rule applies to one game. A rule missing the field its type needs (a close-spread
    /// rule with no threshold, a team rule with no team) never matches; <see cref="PointRuleValidation"/>
    /// is what reports that as an error.
    /// </summary>
    private static bool Matches(PointRuleInfo rule, PointGameInfo game, decimal? currentSpread) =>
        rule.RuleType switch
        {
            PointRuleType.ConferenceGame => MatchesConferenceGame(rule.ConferenceId, game),
            PointRuleType.CloseSpread => rule.SpreadThreshold is { } threshold
                && currentSpread is { } spread
                && Math.Abs(spread) < threshold,
            PointRuleType.Team => rule.TeamId is { } teamId
                && (game.HomeTeamId == teamId || game.AwayTeamId == teamId),
            _ => false,
        };

    /// <summary>
    /// A conference-game rule matches only games the provider flagged as conference games. With no
    /// conference named it matches any of them; with one named, both teams must be in it.
    /// </summary>
    private static bool MatchesConferenceGame(Guid? conferenceId, PointGameInfo game)
    {
        if (!game.IsConferenceGame)
        {
            return false;
        }

        return conferenceId is not { } conference
            || (game.HomeConferenceId == conference && game.AwayConferenceId == conference);
    }

    /// <summary>
    /// Rules in the order they are considered. Already-ascending input is returned untouched; the
    /// sort is defensive, and stable, so callers that pass their list in display order get the same
    /// answer either way.
    /// </summary>
    private static IEnumerable<PointRuleInfo> InPriorityOrder(IReadOnlyList<PointRuleInfo> rules) =>
        IsAscendingByPriority(rules) ? rules : rules.OrderBy(rule => rule.Priority);

    private static bool IsAscendingByPriority(IReadOnlyList<PointRuleInfo> rules)
    {
        for (var i = 1; i < rules.Count; i++)
        {
            if (rules[i].Priority < rules[i - 1].Priority)
            {
                return false;
            }
        }

        return true;
    }
}
