using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// Turns a league's game-set rules into the list of games its members pick that week
/// (Feature 02, <c>04-Domain-Algorithms.md</c> section 2).
/// </summary>
/// <remarks>
/// <para>
/// Pure and static: no clock, no I/O, no entities. Callers project their rows into
/// <see cref="GameSetGenerationRequest"/> and persist whatever comes back.
/// </para>
/// <para>
/// The order of operations is the one the story describes: an eligible pool first (Saturday
/// Eastern, both teams FBS, not postponed or cancelled - "regardless of rule", so it gates manual
/// adds too), then the union of the rules, then existing manual adds, then sticky removals.
/// </para>
/// </remarks>
public static class GameSetGenerator
{
    private static readonly HashSet<Guid> NoRankedTeams = [];

    /// <summary>
    /// Generates the week's set from the saved rules and diffs it against what the set already
    /// holds.
    /// </summary>
    /// <param name="request">The week's games, rules, polls, and current rows.</param>
    /// <returns>
    /// The new set with its <see cref="GenerationResult.Added"/> and removal lists, or
    /// <see cref="GenerationResult.Locked"/> when <see cref="GameSetGenerationRequest.IsLocked"/>
    /// is set. Generation after lock is a no-op that reports itself through a result flag rather
    /// than an exception, so the caller decides whether that is a 409 (an endpoint) or something
    /// to skip quietly (the Tuesday regeneration job).
    /// </returns>
    public static GenerationResult Generate(GameSetGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.IsLocked ? GenerationResult.Locked : Build(request);
    }

    /// <summary>
    /// Runs the same selection against candidate rules without diffing for persistence, so a
    /// commissioner can see what a configuration would produce before saving it.
    /// </summary>
    /// <param name="request">
    /// The same request <see cref="Generate"/> takes, with the candidate rules in
    /// <see cref="GameSetGenerationRequest.Rules"/>. The week's existing rows still apply: manual
    /// adds show up and manually removed games stay out, because that is what saving would do.
    /// </param>
    /// <returns>
    /// The games the rules would produce, with <see cref="GenerationResult.ExceedsMax"/> for the
    /// over-50 warning. A locked week is previewed like any other; only saving is refused. The
    /// diff lists are populated as well, since they cost nothing and a preview may as well show
    /// what would change.
    /// </returns>
    public static GenerationResult Preview(GameSetGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Build(request);
    }

    private static GenerationResult Build(GameSetGenerationRequest request)
    {
        IReadOnlyList<GameInfo> games = request.Games ?? [];
        IReadOnlyList<RuleInfo> rules = request.Rules ?? [];
        IReadOnlyList<ExistingSetGame> existing = request.ExistingGames ?? [];

        // Step 1: the eligible pool. Everything downstream selects out of this, rules and manual
        // adds alike, because the story excludes non-Saturday, non-FBS, postponed and cancelled
        // games "regardless of rule".
        Dictionary<Guid, GameInfo> pool = [];
        foreach (GameInfo game in games)
        {
            if (IsEligible(game))
            {
                pool[game.GameId] = game;
            }
        }

        // Step 2: the poll to rank against, and whether it is a fallback.
        (IReadOnlySet<Guid> rankedTeamIds, bool isFallbackPoll) = SelectRankings(request.Week, request.Rankings ?? []);
        bool usedFallbackRankings = isFallbackPoll && HasTop25Rule(rules);

        // Steps 2 and 3: union of the rule selections, distinct by game id.
        Dictionary<Guid, GameSetGameSource> selected = [];
        foreach (GameInfo game in pool.Values)
        {
            if (MatchesAnyRule(rules, game, rankedTeamIds))
            {
                selected[game.GameId] = GameSetGameSource.Rule;
            }
        }

        // Step 4: manual adds survive whatever the rules say. A game that is both manual and
        // rule-matched stays Manual so it keeps that immunity next time.
        foreach (ExistingSetGame row in existing)
        {
            if (row.Source == GameSetGameSource.Manual && !row.IsRemoved && pool.ContainsKey(row.GameId))
            {
                selected[row.GameId] = GameSetGameSource.Manual;
            }
        }

        // Step 5: sticky removals. A game taken out before lock stays out.
        foreach (ExistingSetGame row in existing)
        {
            if (row.IsRemoved)
            {
                selected.Remove(row.GameId);
            }
        }

        List<GeneratedGame> resultGames = [.. selected
            .Select(entry => new { Game = pool[entry.Key], Source = entry.Value })
            .OrderBy(x => x.Game.KickoffUtc)
            .ThenBy(x => x.Game.GameId)
            .Select(x => new GeneratedGame(x.Game.GameId, x.Source))];

        // Step 9: the week locks at the first kickoff in the set.
        DateTime? lockAtUtc = resultGames.Count == 0 ? null : pool[resultGames[0].GameId].KickoffUtc;

        // Step 7: the regeneration diff, against the rows that are active today.
        HashSet<Guid> activeBefore = [.. existing.Where(row => !row.IsRemoved).Select(row => row.GameId)];

        List<Guid> added = [.. resultGames
            .Where(game => !activeBefore.Contains(game.GameId))
            .Select(game => game.GameId)];

        List<Guid> removedIneligible = [.. existing
            .Where(row => !row.IsRemoved && !selected.ContainsKey(row.GameId) && !pool.ContainsKey(row.GameId))
            .Select(row => row.GameId)
            .Distinct()
            .Order()];

        List<Guid> removed = [.. existing
            .Where(row => !row.IsRemoved
                && row.Source == GameSetGameSource.Rule
                && !selected.ContainsKey(row.GameId)
                && pool.ContainsKey(row.GameId))
            .Select(row => row.GameId)
            .Distinct()
            .Order()];

        return new GenerationResult(
            Games: resultGames,
            Added: added,
            Removed: removed,
            RemovedIneligible: removedIneligible,
            ExceedsMax: resultGames.Count > GameSetLimits.MaxGames,
            UsedFallbackRankings: usedFallbackRankings,
            LockAtUtc: lockAtUtc,
            IsLocked: false);
    }

    /// <summary>Step 1: Saturday Eastern, both teams FBS, still on the schedule.</summary>
    private static bool IsEligible(GameInfo game) =>
        game.IsSaturdayEastern
        && game.HomeClassification == TeamClassification.Fbs
        && game.AwayClassification == TeamClassification.Fbs
        && game.Status is not (GameStatus.Postponed or GameStatus.Cancelled);

    private static bool HasTop25Rule(IReadOnlyList<RuleInfo> rules)
    {
        foreach (RuleInfo rule in rules)
        {
            if (rule.Type == GameSetRuleType.Top25)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAnyRule(IReadOnlyList<RuleInfo> rules, GameInfo game, IReadOnlySet<Guid> rankedTeamIds)
    {
        foreach (RuleInfo rule in rules)
        {
            if (Matches(rule, game, rankedTeamIds))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(RuleInfo rule, GameInfo game, IReadOnlySet<Guid> rankedTeamIds) => rule.Type switch
    {
        // Either team ranked in the poll this week's generation is using.
        GameSetRuleType.Top25 =>
            rankedTeamIds.Contains(game.HomeTeamId) || rankedTeamIds.Contains(game.AwayTeamId),

        // Both teams in the conference when the toggle is on, either team otherwise.
        GameSetRuleType.Conference => rule.ConferenceId is Guid conferenceId
            && (rule.ConferenceGamesOnly
                ? game.HomeConferenceId == conferenceId && game.AwayConferenceId == conferenceId
                : game.HomeConferenceId == conferenceId || game.AwayConferenceId == conferenceId),

        // Home or away. A bye week simply has no game to match.
        GameSetRuleType.Team => rule.TeamId is Guid teamId
            && (game.HomeTeamId == teamId || game.AwayTeamId == teamId),

        _ => false,
    };

    /// <summary>
    /// Step 2: the poll for this week with the latest fetch, else the most recent prior week's
    /// poll (flagged), else nothing ranked at all.
    /// </summary>
    private static (IReadOnlySet<Guid> RankedTeamIds, bool IsFallback) SelectRankings(
        int week,
        IReadOnlyList<RankingSet> rankings)
    {
        RankingSet? current = rankings
            .Where(poll => poll.Week == week)
            .OrderByDescending(poll => poll.FetchedUtc)
            .FirstOrDefault();

        if (current is not null)
        {
            return (current.RankedTeamIds ?? NoRankedTeams, false);
        }

        RankingSet? prior = rankings
            .Where(poll => poll.Week < week)
            .OrderByDescending(poll => poll.Week)
            .ThenByDescending(poll => poll.FetchedUtc)
            .FirstOrDefault();

        return prior is null
            ? (NoRankedTeams, false)
            : (prior.RankedTeamIds ?? NoRankedTeams, true);
    }
}
