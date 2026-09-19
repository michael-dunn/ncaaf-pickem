using NcaafPickEm.Shared.Contracts.Leaderboard;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Leaderboard;

/// <summary>
/// The whole of <c>04-Domain-Algorithms.md</c> section 8 (Feature 07): season standings, a week's
/// standings, the week grid, and the snapshot rows the scorer freezes when a week completes.
/// Pure - no clock, no I/O, no entities - over the flattened records in this folder.
/// </summary>
/// <remarks>
/// Ranking is always "competition" ranking ("1224"): equal scores share a rank and the next rank
/// skips. Rows are ordered by score descending, then by display name (case-insensitively), then by
/// membership id, so two runs over the same inputs produce the same list. The rows this returns are
/// the <c>Shared/Contracts/Leaderboard</c> records the endpoints serve verbatim (D-140).
/// </remarks>
public static class StandingsCalculator
{
    /// <summary>
    /// The season leaderboard: one row per member in <paramref name="activeMembers"/>, ranked by
    /// total points.
    /// </summary>
    /// <param name="activeMembers">The league's active memberships (<c>RemovedUtc == null</c>).
    /// Former members are deliberately absent: they stay on week leaderboards only.</param>
    /// <param name="results">Every week result in the league, for any membership. Rows belonging to
    /// a membership that is not listed, or to a week before that member's
    /// <see cref="StandingsMember.JoinedWeek"/>, are ignored.</param>
    /// <param name="snapshots">Every <c>SeasonStandingsSnapshots</c> row for the league; the two
    /// most recent <c>ThroughWeek</c> values are what the trend arrows compare.</param>
    /// <param name="viewerMembershipId">The caller's membership, for <c>IsMe</c>; null for none.</param>
    /// <returns>Rows ordered by rank, then name.</returns>
    public static SeasonRow[] SeasonRows(
        IReadOnlyList<StandingsMember> activeMembers,
        IReadOnlyList<StandingsWeekResult> results,
        IReadOnlyList<StandingsSnapshot> snapshots,
        Guid? viewerMembershipId)
    {
        ArgumentNullException.ThrowIfNull(activeMembers);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(snapshots);

        Dictionary<Guid, StandingsMember> byId = ByMembershipId(activeMembers);
        Dictionary<Guid, int> totals = Totals(byId, results);
        Dictionary<Guid, int> weeklyWins = WeeklyWins(byId, results);
        Dictionary<Guid, StandingsTrend> trends = Trends(snapshots);

        (StandingsMember Member, int Total)[] ordered =
        [
            .. activeMembers
                .Select(member => (Member: member, Total: totals.GetValueOrDefault(member.MembershipId)))
                .OrderByDescending(entry => entry.Total)
                .ThenBy(entry => entry.Member.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Member.MembershipId)
        ];

        int leaderTotal = ordered.Length == 0 ? 0 : ordered[0].Total;
        int[] ranks = CompetitionRanks([.. ordered.Select(entry => entry.Total)]);

        var rows = new SeasonRow[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            (StandingsMember member, int total) = ordered[i];

            rows[i] = new SeasonRow(
                ranks[i],
                member.MembershipId,
                member.DisplayName,
                total,
                PointsBehind: leaderTotal - total,
                WeeklyWins: weeklyWins.GetValueOrDefault(member.MembershipId),
                Trend: trends.GetValueOrDefault(member.MembershipId, StandingsTrend.None),
                IsMe: viewerMembershipId == member.MembershipId);
        }

        return rows;
    }

    /// <summary>
    /// One week's leaderboard: every member in <paramref name="members"/> who has a result row for
    /// <paramref name="week"/>, former members included, ranked by that week's points.
    /// </summary>
    /// <param name="week">The week.</param>
    /// <param name="members">Every membership that may appear - for a week that means everyone with
    /// a result row, whether or not they are still in the league.</param>
    /// <param name="results">Results for any week; only <paramref name="week"/>'s are used.</param>
    /// <param name="isComplete">The set's complete flag. A week that is not complete has no winner
    /// yet and the client labels it provisional.</param>
    /// <param name="viewerMembershipId">The caller's membership, for <c>IsMe</c>; null for none.</param>
    /// <returns>Rows ordered by rank, then name.</returns>
    public static WeekRow[] WeekRows(
        int week,
        IReadOnlyList<StandingsMember> members,
        IReadOnlyList<StandingsWeekResult> results,
        bool isComplete,
        Guid? viewerMembershipId)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(results);

        Dictionary<Guid, StandingsMember> byId = ByMembershipId(members);

        (StandingsMember Member, StandingsWeekResult Result)[] ordered =
        [
            .. results
                .Where(result => result.Week == week)
                .Select(result => (Member: byId.GetValueOrDefault(result.MembershipId), Result: result))
                .Where(entry => entry.Member is not null && entry.Result.Week >= entry.Member.JoinedWeek)
                .Select(entry => (Member: entry.Member!, entry.Result))
                .OrderByDescending(entry => entry.Result.Points)
                .ThenBy(entry => entry.Member.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Member.MembershipId)
        ];

        int maxPoints = ordered.Length == 0 ? 0 : ordered[0].Result.Points;
        int[] ranks = CompetitionRanks([.. ordered.Select(entry => entry.Result.Points)]);

        var rows = new WeekRow[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            (StandingsMember member, StandingsWeekResult result) = ordered[i];

            rows[i] = new WeekRow(
                ranks[i],
                member.MembershipId,
                member.DisplayName,
                result.Points,
                result.CorrectCount,
                result.ActiveGameCount,
                IsWinner: isComplete && result.Points == maxPoints,
                IsFormer: member.IsFormer,
                IsMe: viewerMembershipId == member.MembershipId);
        }

        return rows;
    }

    /// <summary>
    /// The week grid's cells, one per game per member, in game order then member order.
    /// </summary>
    /// <param name="games">Active rows of the locked set, voided ones included, in the order the
    /// grid renders them (kickoff).</param>
    /// <param name="membersWithSubmission">The memberships with a <c>WeekSubmissions</c> row for the
    /// week - who was in the league at lock, former members included - in column order.</param>
    /// <param name="picks">Every pick those members made on those games. Anything else is ignored.</param>
    /// <returns>Cells with outcome precedence <c>Voided</c> &gt; <c>NoPick</c> &gt; <c>Pending</c>
    /// &gt; <c>Correct</c>/<c>Incorrect</c>.</returns>
    public static GridCell[] GridCells(
        IReadOnlyList<GridGame> games,
        IReadOnlyList<StandingsMember> membersWithSubmission,
        IReadOnlyList<GridPick> picks)
    {
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(membersWithSubmission);
        ArgumentNullException.ThrowIfNull(picks);

        var picked = new Dictionary<(Guid GameSetGameId, Guid MembershipId), Guid>();
        foreach (GridPick pick in picks)
        {
            picked[(pick.GameSetGameId, pick.MembershipId)] = pick.TeamId;
        }

        var cells = new GridCell[games.Count * membersWithSubmission.Count];
        int next = 0;

        foreach (GridGame game in games)
        {
            foreach (StandingsMember member in membersWithSubmission)
            {
                Guid? teamId = picked.TryGetValue((game.GameSetGameId, member.MembershipId), out Guid value)
                    ? value
                    : null;

                GridOutcome outcome = game.IsVoided
                    ? GridOutcome.Voided
                    : teamId is null
                        ? GridOutcome.NoPick
                        : game.WinnerTeamId is not Guid winner
                            ? GridOutcome.Pending
                            : teamId == winner ? GridOutcome.Correct : GridOutcome.Incorrect;

                cells[next++] = new GridCell(game.GameSetGameId, member.MembershipId, teamId, outcome);
            }
        }

        return cells;
    }

    /// <summary>
    /// The rows the snapshot writer persists for <paramref name="throughWeek"/>: the season
    /// standings as they stand once every week up to and including that one is counted. Exactly
    /// <see cref="SeasonRows"/>' ranking, so a trend arrow compares like with like.
    /// </summary>
    /// <param name="members">The league's active memberships.</param>
    /// <param name="results">Every week result in the league; later weeks are dropped.</param>
    /// <param name="throughWeek">The week that just became complete.</param>
    /// <returns>One row per member, ordered by rank.</returns>
    public static StandingsSnapshotRow[] ComputeSnapshot(
        IReadOnlyList<StandingsMember> members,
        IReadOnlyList<StandingsWeekResult> results,
        int throughWeek)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(results);

        StandingsWeekResult[] through = [.. results.Where(result => result.Week <= throughWeek)];

        return
        [
            .. SeasonRows(members, through, [], viewerMembershipId: null)
                .Select(row => new StandingsSnapshotRow(row.MembershipId, row.Rank, row.TotalPoints))
        ];
    }

    private static Dictionary<Guid, StandingsMember> ByMembershipId(IReadOnlyList<StandingsMember> members)
    {
        var byId = new Dictionary<Guid, StandingsMember>(members.Count);
        foreach (StandingsMember member in members)
        {
            byId[member.MembershipId] = member;
        }

        return byId;
    }

    private static Dictionary<Guid, int> Totals(
        Dictionary<Guid, StandingsMember> byId,
        IReadOnlyList<StandingsWeekResult> results)
    {
        var totals = new Dictionary<Guid, int>(byId.Count);
        foreach (StandingsWeekResult result in results)
        {
            if (!byId.TryGetValue(result.MembershipId, out StandingsMember? member) || result.Week < member.JoinedWeek)
            {
                continue;
            }

            totals[result.MembershipId] = totals.GetValueOrDefault(result.MembershipId) + result.Points;
        }

        return totals;
    }

    /// <summary>
    /// Complete weeks where the member's points equal that week's maximum. The maximum is taken over
    /// every result row of the week, former members included, so a week won by somebody who has
    /// since left is not handed to the best remaining member; ties share the win.
    /// </summary>
    private static Dictionary<Guid, int> WeeklyWins(
        Dictionary<Guid, StandingsMember> byId,
        IReadOnlyList<StandingsWeekResult> results)
    {
        var wins = new Dictionary<Guid, int>(byId.Count);

        foreach (IGrouping<int, StandingsWeekResult> week in results.GroupBy(result => result.Week))
        {
            if (!week.All(result => result.IsWeekComplete))
            {
                continue;
            }

            int maxPoints = week.Max(result => result.Points);

            foreach (StandingsWeekResult result in week)
            {
                if (result.Points != maxPoints
                    || !byId.TryGetValue(result.MembershipId, out StandingsMember? member)
                    || result.Week < member.JoinedWeek)
                {
                    continue;
                }

                wins[result.MembershipId] = wins.GetValueOrDefault(result.MembershipId) + 1;
            }
        }

        return wins;
    }

    /// <summary>
    /// Rank movement between the two most recent snapshot weeks. Snapshots exist only for complete
    /// weeks, so "the latest two <c>ThroughWeek</c> values" is the same question as "the latest two
    /// complete weeks". A member missing from either snapshot - and everybody, when there is only
    /// one snapshot week - reads <see cref="StandingsTrend.None"/>.
    /// </summary>
    private static Dictionary<Guid, StandingsTrend> Trends(IReadOnlyList<StandingsSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return [];
        }

        int latestWeek = snapshots.Max(snapshot => snapshot.ThroughWeek);

        int[] earlierWeeks = [.. snapshots
            .Where(snapshot => snapshot.ThroughWeek < latestWeek)
            .Select(snapshot => snapshot.ThroughWeek)];

        if (earlierWeeks.Length == 0)
        {
            return [];
        }

        int previousWeek = earlierWeeks.Max();

        Dictionary<Guid, int> previousRanks = snapshots
            .Where(snapshot => snapshot.ThroughWeek == previousWeek)
            .ToDictionary(snapshot => snapshot.MembershipId, snapshot => snapshot.Rank);

        var trends = new Dictionary<Guid, StandingsTrend>();
        foreach (StandingsSnapshot snapshot in snapshots.Where(candidate => candidate.ThroughWeek == latestWeek))
        {
            if (!previousRanks.TryGetValue(snapshot.MembershipId, out int previousRank))
            {
                continue;
            }

            trends[snapshot.MembershipId] = snapshot.Rank < previousRank
                ? StandingsTrend.Up
                : snapshot.Rank > previousRank ? StandingsTrend.Down : StandingsTrend.Same;
        }

        return trends;
    }

    /// <summary>Competition ranking ("1224") over scores already sorted descending.</summary>
    private static int[] CompetitionRanks(IReadOnlyList<int> scoresInOrder)
    {
        var ranks = new int[scoresInOrder.Count];
        for (int i = 0; i < scoresInOrder.Count; i++)
        {
            ranks[i] = i > 0 && scoresInOrder[i] == scoresInOrder[i - 1] ? ranks[i - 1] : i + 1;
        }

        return ranks;
    }
}
