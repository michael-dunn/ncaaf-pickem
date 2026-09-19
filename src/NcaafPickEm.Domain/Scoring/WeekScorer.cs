using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Scoring;

/// <summary>
/// Scores one locked week of one league (Feature 06, <c>04-Domain-Algorithms.md</c> section 7).
/// Pure: no clock, no I/O, no entities.
/// </summary>
/// <remarks>
/// <para>
/// A full recompute from source rows, never an incremental update (D-006). Nothing here reads or
/// writes what a previous run produced, so scoring the same week twice - after every game, after
/// a correction, or from the nightly sweep - lands on the same answer.
/// </para>
/// <para>
/// Who won a game is <see cref="WinnerResolver"/>'s question and nobody else's (D-098): the
/// commissioner's override if there is one, else the higher score once the game is Final. A Final
/// tie, or a Final game missing a score, has no winner - it scores for nobody and goes on the
/// needs-review list rather than counting as a loss for everybody.
/// </para>
/// <para>
/// A week with no active games at all - every row voided - comes back complete, with everybody on
/// zero out of zero. There is nothing left to wait for, which is the same answer the last void
/// would have produced one game at a time.
/// </para>
/// </remarks>
public static class WeekScorer
{
    /// <summary>
    /// Recomputes every member's score for one week.
    /// </summary>
    /// <param name="request">The week's rows, its settled memberships, and their picks.</param>
    /// <returns>
    /// One <see cref="MemberWeekScore"/> per settled membership, whether the week is complete, and
    /// the active rows that need a commissioner's decision.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static WeekScoreResult Score(WeekScoringRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Active = neither removed before lock (D-008) nor voided after it (Feature 06). Both are
        // out of the scoring, out of ActiveGameCount, and out of the completeness question.
        List<ScoringGame> activeGames = [.. request.Games.Where(game => game.IsActive)];

        Dictionary<Guid, Guid> winnerByGame = [];
        List<Guid> needsReview = [];
        bool isWeekComplete = true;

        foreach (ScoringGame game in activeGames)
        {
            Guid? winner = WinnerResolver.Resolve(
                game.ResultOverrideWinnerTeamId,
                game.Status,
                game.HomeScore,
                game.AwayScore,
                game.HomeTeamId,
                game.AwayTeamId);

            if (winner is { } decided)
            {
                winnerByGame[game.GameSetGameId] = decided;
                continue;
            }

            // No winner yet. A game still to be played simply keeps the week open; a game that is
            // Final without one needs a person to decide, and keeps the week open until they do.
            isWeekComplete = false;

            if (game.Status == GameStatus.Final)
            {
                needsReview.Add(game.GameSetGameId);
            }
        }

        Dictionary<Guid, Dictionary<Guid, Guid>> picksByMember = [];
        foreach (ScoringPick pick in request.Picks)
        {
            if (!winnerByGame.ContainsKey(pick.GameSetGameId))
            {
                // A pick on a removed, voided, or still-undecided game can never earn anything,
                // so it never has to be indexed. It stays in the Picks table for history either
                // way.
                continue;
            }

            if (!picksByMember.TryGetValue(pick.MembershipId, out Dictionary<Guid, Guid>? forMember))
            {
                forMember = [];
                picksByMember[pick.MembershipId] = forMember;
            }

            forMember[pick.GameSetGameId] = pick.TeamId;
        }

        List<MemberWeekScore> scores = [];
        foreach (ScoringMember member in request.Members)
        {
            if (!member.WasActiveAtLock)
            {
                continue;
            }

            int points = 0;
            int correct = 0;

            if (picksByMember.TryGetValue(member.MembershipId, out Dictionary<Guid, Guid>? picked))
            {
                foreach (ScoringGame game in activeGames)
                {
                    if (picked.TryGetValue(game.GameSetGameId, out Guid pickedTeamId)
                        && winnerByGame.TryGetValue(game.GameSetGameId, out Guid winnerTeamId)
                        && pickedTeamId == winnerTeamId)
                    {
                        points += game.PointValue;
                        correct++;
                    }
                }
            }

            scores.Add(new MemberWeekScore(member.MembershipId, points, correct, activeGames.Count));
        }

        return new WeekScoreResult(scores, isWeekComplete, needsReview);
    }
}
