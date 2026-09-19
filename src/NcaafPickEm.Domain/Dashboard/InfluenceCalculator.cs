using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Dashboard;

/// <summary>
/// Builds one member's influence dashboard for one locked week (Feature 05,
/// <c>04-Domain-Algorithms.md</c> section 6). Pure: no clock, no I/O, no entities, no mutation.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard answers one question per game - "how much of the league is on the other side of
/// this?" - and orders the week by the answer. The viewer never appears in any of their own lists;
/// everyone else is listed in the order <see cref="InfluenceRequest.MembersActiveAtLock"/> gave.
/// </para>
/// <para>
/// Two exclusions are deliberate. <strong>Voided games</strong> are left out of both lists and out
/// of both header totals: section 6 works over the <em>active</em> games in the locked set, and a
/// void is how a game stops being active after lock (section 7 excludes it from scoring for the
/// same reason). <strong>Picks from unlisted memberships</strong> are ignored: the service decides
/// who was active at lock, and the calculator never looks past that list, so a member who joined
/// after lock cannot appear even if their pick rows are handed in.
/// </para>
/// </remarks>
public static class InfluenceCalculator
{
    /// <summary>
    /// Builds the dashboard.
    /// </summary>
    /// <param name="request">Viewer, games, members active at lock, and their picks.</param>
    /// <returns>The ordered games, the collapsed "everyone agrees" games, and the header totals.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> or one of its lists is null.</exception>
    public static InfluenceResult Calculate(InfluenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Games);
        ArgumentNullException.ThrowIfNull(request.MembersActiveAtLock);
        ArgumentNullException.ThrowIfNull(request.Picks);

        IReadOnlyList<InfluenceMember> others = Others(request);
        Dictionary<(Guid MembershipId, Guid GameSetGameId), Guid> picks = IndexPicks(request);

        List<(InfluenceGame Game, InfluenceGameResult Result)> scored =
        [
            .. request.Games
                .Where(game => !game.IsVoided)
                .Select(game => (game, Describe(game, request, others, picks))),
        ];

        return new InfluenceResult(
            Games: Ordered(scored.Where(entry => !BelongsInEveryoneAgrees(entry.Result))),
            EveryoneAgrees: Ordered(scored.Where(entry => BelongsInEveryoneAgrees(entry.Result))),
            PointsSoFar: scored
                .Where(entry => entry.Result.MyOutcome == InfluenceOutcome.Won)
                .Sum(entry => entry.Game.PointValue),
            MaxRemaining: scored
                .Where(entry => IsStillInPlay(entry.Game, entry.Result))
                .Sum(entry => entry.Game.PointValue));
    }

    /// <summary>
    /// Describes one game from the viewer's side: who is against them, who never picked, what the
    /// two teams' support looks like, and where the game stands.
    /// </summary>
    private static InfluenceGameResult Describe(
        InfluenceGame game,
        InfluenceRequest request,
        IReadOnlyList<InfluenceMember> others,
        Dictionary<(Guid, Guid), Guid> picks)
    {
        Guid? myTeamId = picks.TryGetValue((request.ViewerMembershipId, game.GameSetGameId), out Guid mine)
            ? mine
            : null;

        List<InfluenceMember> oppositePicks = [];
        List<InfluenceMember> noPick = [];
        List<InfluenceMember> homePickers = [];
        List<InfluenceMember> awayPickers = [];

        foreach (InfluenceMember member in others)
        {
            if (!picks.TryGetValue((member.MembershipId, game.GameSetGameId), out Guid theirTeamId))
            {
                noPick.Add(member);
                continue;
            }

            if (theirTeamId == game.HomeTeamId)
            {
                homePickers.Add(member);
            }
            else if (theirTeamId == game.AwayTeamId)
            {
                awayPickers.Add(member);
            }

            // With no team of their own the viewer has nobody to be opposite to, so the list stays
            // empty and the UI shows both teams' pickers instead.
            if (myTeamId is { } viewerTeamId && theirTeamId != viewerTeamId)
            {
                oppositePicks.Add(member);
            }
        }

        Guid? winnerTeamId = WinnerResolver.Resolve(
            game.ResultOverrideWinnerTeamId,
            game.Status,
            game.HomeScore,
            game.AwayScore,
            game.HomeTeamId,
            game.AwayTeamId);

        return new InfluenceGameResult(
            GameSetGameId: game.GameSetGameId,
            MyTeamId: myTeamId,
            MyOutcome: Outcome(myTeamId, winnerTeamId),
            OppositeCount: oppositePicks.Count,
            OppositePicks: oppositePicks,
            NoPick: noPick,
            HomePickers: homePickers,
            AwayPickers: awayPickers,
            SwingPoints: game.PointValue * oppositePicks.Count,
            WinnerTeamId: winnerTeamId);
    }

    /// <summary>
    /// No pick is <see cref="InfluenceOutcome.NoPick"/> forever; a pick is Won or Lost as soon as
    /// the game has a winner - by final score or by a commissioner's override - and Pending until
    /// then. A Final tie or a Final game with a score missing has no winner, so it reads as Pending,
    /// which is the "needs review" path, not a loss.
    /// </summary>
    private static InfluenceOutcome Outcome(Guid? myTeamId, Guid? winnerTeamId) =>
        (myTeamId, winnerTeamId) switch
        {
            (null, _) => InfluenceOutcome.NoPick,
            (_, null) => InfluenceOutcome.Pending,
            var (mine, winner) => mine == winner ? InfluenceOutcome.Won : InfluenceOutcome.Lost,
        };

    /// <summary>
    /// A game counts towards "max remaining" while the viewer has a pick on it, it has not gone
    /// Final, and it has no winner yet. The last clause is what keeps an overridden result from
    /// being counted as both already-earned and still-to-come.
    /// </summary>
    private static bool IsStillInPlay(InfluenceGame game, InfluenceGameResult result) =>
        result.MyTeamId is not null
        && game.Status != GameStatus.Final
        && result.MyOutcome == InfluenceOutcome.Pending;

    /// <summary>
    /// Nobody picked against the viewer - but only a game the viewer actually picked is collapsed
    /// away. A game they skipped has a zero count for want of a pick, not for want of disagreement,
    /// and the story keeps it in the main list so both teams' pickers are on show.
    /// </summary>
    private static bool BelongsInEveryoneAgrees(InfluenceGameResult result) =>
        result.OppositeCount == 0 && result.MyOutcome != InfluenceOutcome.NoPick;

    /// <summary>
    /// Most opposition first, then the bigger point value, then the earlier kickoff, then the row
    /// id so two runs over the same inputs produce the same list.
    /// </summary>
    private static IReadOnlyList<InfluenceGameResult> Ordered(
        IEnumerable<(InfluenceGame Game, InfluenceGameResult Result)> entries) =>
    [
        .. entries
            .OrderByDescending(entry => entry.Result.OppositeCount)
            .ThenByDescending(entry => entry.Game.PointValue)
            .ThenBy(entry => entry.Game.KickoffUtc)
            .ThenBy(entry => entry.Game.GameSetGameId)
            .Select(entry => entry.Result),
    ];

    /// <summary>
    /// Everybody but the viewer, in the caller's order. The viewer is dropped once, here, so no
    /// list downstream can accidentally name them.
    /// </summary>
    private static IReadOnlyList<InfluenceMember> Others(InfluenceRequest request) =>
    [
        .. request.MembersActiveAtLock
            .Where(member => member.MembershipId != request.ViewerMembershipId),
    ];

    /// <summary>
    /// Picks keyed by member and game, keeping only picks made by a member who was active at lock
    /// on a game that is still in the set. The viewer counts as such a member whether or not the
    /// caller listed them - the list decides who can be <em>named</em>, not whose dashboard this is.
    /// A duplicate (which the unique index on <c>Picks(MembershipId, WeekGameSetGameId)</c> makes
    /// impossible in the database) keeps the first one rather than letting input order decide the
    /// answer.
    /// </summary>
    private static Dictionary<(Guid MembershipId, Guid GameSetGameId), Guid> IndexPicks(
        InfluenceRequest request)
    {
        HashSet<Guid> memberIds =
        [
            request.ViewerMembershipId,
            .. request.MembersActiveAtLock.Select(member => member.MembershipId),
        ];
        HashSet<Guid> gameIds = [.. request.Games.Select(game => game.GameSetGameId)];
        Dictionary<(Guid, Guid), Guid> picks = [];

        foreach (InfluencePick pick in request.Picks)
        {
            if (memberIds.Contains(pick.MembershipId) && gameIds.Contains(pick.GameSetGameId))
            {
                picks.TryAdd((pick.MembershipId, pick.GameSetGameId), pick.TeamId);
            }
        }

        return picks;
    }
}
