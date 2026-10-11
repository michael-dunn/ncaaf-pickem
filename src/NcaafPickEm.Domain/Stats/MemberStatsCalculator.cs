using NcaafPickEm.Domain.Leaderboard;
using NcaafPickEm.Shared.Contracts.Stats;

namespace NcaafPickEm.Domain.Stats;

/// <summary>
/// The whole of <c>04-Domain-Algorithms.md</c> section 13 (Feature 14): the league stats page's
/// category rankings and one member's stats profile. Pure - no clock, no I/O, no entities - over
/// the flattened records in this folder and <c>Domain/Leaderboard</c>.
/// </summary>
/// <remarks>
/// A member is measured on "their weeks": the weeks they have a <c>WeekResults</c> row for, from
/// their <see cref="StandingsMember.JoinedWeek"/> on. That row exists exactly when the lock job
/// settled them for the week (D-111), so a late joiner is not charged with games from before they
/// joined and a former member keeps the weeks they played. Within those weeks a counted game is a
/// <see cref="StatsGame"/> with a winner; a counted game the member did not pick is a miss.
/// Categories rank by percentage with competition ranking ("1224"), compared as exact fractions,
/// then by display name case-insensitively, then by membership id - the leaderboard's order.
/// </remarks>
public static class MemberStatsCalculator
{
    /// <summary>The league stats page: every active member ranked in each category.</summary>
    /// <param name="activeMembers">The league's active memberships. Former members are absent.</param>
    /// <param name="games">Active rows of the league's locked sets, decided or not.</param>
    /// <param name="picks">Picks on those rows, for any membership.</param>
    /// <param name="results">Every <c>WeekResults</c> row of the league, for any membership.</param>
    /// <param name="viewerMembershipId">The caller's membership, for <c>IsMe</c>; null for none.</param>
    /// <returns>Both categories, rows ordered by rank then name.</returns>
    public static LeagueStats LeagueStats(
        IReadOnlyList<StandingsMember> activeMembers,
        IReadOnlyList<StatsGame> games,
        IReadOnlyList<StatsPick> picks,
        IReadOnlyList<StandingsWeekResult> results,
        Guid? viewerMembershipId)
    {
        ArgumentNullException.ThrowIfNull(activeMembers);
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(picks);
        ArgumentNullException.ThrowIfNull(results);

        StatsGame[] counted = Counted(games);
        Dictionary<(Guid MembershipId, Guid GameSetGameId), Guid> picked = PickLookup(picks);

        Tally[] tallies = [.. activeMembers.Select(member => TallyOf(member, counted, picked, results))];

        return new LeagueStats(
            ThroughWeek: counted.Length == 0 ? null : counted.Max(game => game.Week),
            IsProvisional: counted.Any(game => !game.IsWeekComplete),
            Accuracy: RankRows(tallies, tally => tally.Accuracy, viewerMembershipId),
            Underdog: RankRows(tallies, tally => tally.Underdog, viewerMembershipId));
    }

    /// <summary>One member's stats profile, former members included.</summary>
    /// <param name="member">The member whose profile this is.</param>
    /// <param name="activeMembers">The league's active memberships, for the category ranks.</param>
    /// <param name="games">Active rows of the league's locked sets, decided or not.</param>
    /// <param name="picks">Picks on those rows, for any membership.</param>
    /// <param name="results">Every <c>WeekResults</c> row of the league, for any membership; the
    /// weekly rank is taken over all of a week's rows, former members included, as the week
    /// leaderboard does.</param>
    /// <param name="viewerMembershipId">The caller's membership, for <c>IsMe</c>.</param>
    /// <returns>The profile.</returns>
    public static MemberStatsProfile Profile(
        StandingsMember member,
        IReadOnlyList<StandingsMember> activeMembers,
        IReadOnlyList<StatsGame> games,
        IReadOnlyList<StatsPick> picks,
        IReadOnlyList<StandingsWeekResult> results,
        Guid? viewerMembershipId)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(activeMembers);
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(picks);
        ArgumentNullException.ThrowIfNull(results);

        LeagueStats league = LeagueStats(activeMembers, games, picks, results, viewerMembershipId);

        StatsGame[] counted = Counted(games);
        Tally tally = TallyOf(member, counted, PickLookup(picks), results);

        StandingsWeekResult[] mine = [.. MemberResults(member, results).OrderBy(result => result.Week)];
        HashSet<int> myWeeks = [.. mine.Select(result => result.Week)];

        MemberWeekStat[] weeks =
        [
            .. mine.Select(result => new MemberWeekStat(
                result.Week,
                result.CorrectCount,
                result.ActiveGameCount,
                result.Points,
                Rank: 1 + results.Count(other => other.Week == result.Week && other.Points > result.Points),
                result.IsWeekComplete))
        ];

        StandingsWeekResult[] complete = [.. mine.Where(result => result.IsWeekComplete)];

        return new MemberStatsProfile(
            member.MembershipId,
            member.DisplayName,
            member.IsFormer,
            IsMe: viewerMembershipId == member.MembershipId,
            IsProvisional: counted.Any(game => myWeeks.Contains(game.Week) && !game.IsWeekComplete),
            Accuracy: StatOf(member, tally.Accuracy, league.Accuracy),
            Underdog: StatOf(member, tally.Underdog, league.Underdog),
            MostCorrect: Highlight(complete, result => result.CorrectCount, most: true),
            LeastCorrect: Highlight(complete, result => result.CorrectCount, most: false),
            MostPoints: Highlight(complete, result => result.Points, most: true),
            LeastPoints: Highlight(complete, result => result.Points, most: false),
            weeks);
    }

    private static StatsGame[] Counted(IReadOnlyList<StatsGame> games) =>
        [.. games.Where(game => game.WinnerTeamId is not null)];

    private static Dictionary<(Guid MembershipId, Guid GameSetGameId), Guid> PickLookup(IReadOnlyList<StatsPick> picks)
    {
        var picked = new Dictionary<(Guid MembershipId, Guid GameSetGameId), Guid>(picks.Count);
        foreach (StatsPick pick in picks)
        {
            picked[(pick.MembershipId, pick.GameSetGameId)] = pick.TeamId;
        }

        return picked;
    }

    private static IEnumerable<StandingsWeekResult> MemberResults(
        StandingsMember member,
        IReadOnlyList<StandingsWeekResult> results) =>
        results.Where(result => result.MembershipId == member.MembershipId && result.Week >= member.JoinedWeek);

    private static Tally TallyOf(
        StandingsMember member,
        StatsGame[] counted,
        Dictionary<(Guid MembershipId, Guid GameSetGameId), Guid> picked,
        IReadOnlyList<StandingsWeekResult> results)
    {
        HashSet<int> weeks = [.. MemberResults(member, results).Select(result => result.Week)];

        int correct = 0, total = 0, underdogHits = 0, underdogWins = 0;
        foreach (StatsGame game in counted)
        {
            if (!weeks.Contains(game.Week))
            {
                continue;
            }

            bool hasPick = picked.TryGetValue((member.MembershipId, game.GameSetGameId), out Guid teamId);
            bool isCorrect = hasPick && teamId == game.WinnerTeamId;

            total++;
            if (isCorrect)
            {
                correct++;
            }

            if (game.UnderdogTeamId is { } underdog && underdog == game.WinnerTeamId)
            {
                underdogWins++;
                if (isCorrect)
                {
                    underdogHits++;
                }
            }
        }

        return new Tally(member, new Fraction(correct, total), new Fraction(underdogHits, underdogWins));
    }

    private static StatRow[] RankRows(Tally[] tallies, Func<Tally, Fraction> category, Guid? viewerMembershipId)
    {
        Tally[] ordered =
        [
            .. tallies
                .OrderByDescending(category)
                .ThenBy(tally => tally.Member.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(tally => tally.Member.MembershipId)
        ];

        var rows = new StatRow[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            Fraction value = category(ordered[i]);
            int rank = i > 0 && value.CompareTo(category(ordered[i - 1])) == 0 ? rows[i - 1].Rank : i + 1;

            rows[i] = new StatRow(
                rank,
                ordered[i].Member.MembershipId,
                ordered[i].Member.DisplayName,
                value.Hits,
                value.Total,
                IsMe: viewerMembershipId == ordered[i].Member.MembershipId);
        }

        return rows;
    }

    private static MemberStat StatOf(StandingsMember member, Fraction value, StatRow[] leagueRows)
    {
        StatRow? row = leagueRows.FirstOrDefault(candidate => candidate.MembershipId == member.MembershipId);
        return new MemberStat(row?.Rank, leagueRows.Length, value.Hits, value.Total);
    }

    /// <summary>
    /// The highest (or lowest) Complete week by <paramref name="measure"/>; ties go to the most
    /// recent week and list the others, most recent first.
    /// </summary>
    private static WeekHighlight? Highlight(
        StandingsWeekResult[] complete,
        Func<StandingsWeekResult, int> measure,
        bool most)
    {
        if (complete.Length == 0)
        {
            return null;
        }

        int value = most ? complete.Max(measure) : complete.Min(measure);
        StandingsWeekResult[] tied = [.. complete.Where(result => measure(result) == value).OrderByDescending(result => result.Week)];

        return new WeekHighlight(
            tied[0].Week,
            value,
            tied[0].ActiveGameCount,
            [.. tied.Skip(1).Select(result => result.Week)]);
    }

    private sealed record Tally(StandingsMember Member, Fraction Accuracy, Fraction Underdog);

    /// <summary>
    /// <c>Hits / Total</c> compared exactly by cross-multiplying, so 2/3 and 4/6 tie and no
    /// floating-point rounding decides a rank. A zero total reads as 0.
    /// </summary>
    private readonly record struct Fraction(int Hits, int Total) : IComparable<Fraction>
    {
        public int CompareTo(Fraction other)
        {
            long left = (long)(Total == 0 ? 0 : Hits) * Math.Max(other.Total, 1);
            long right = (long)(other.Total == 0 ? 0 : other.Hits) * Math.Max(Total, 1);
            return left.CompareTo(right);
        }
    }
}
