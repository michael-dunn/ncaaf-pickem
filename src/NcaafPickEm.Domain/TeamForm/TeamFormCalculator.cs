using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.TeamForm;

/// <summary>
/// <c>04-Domain-Algorithms.md</c> section 14 (Feature 17): a team's last games before a kickoff.
/// Pure - no clock, no I/O, no entities.
/// </summary>
/// <remarks>
/// Counting back from the picked game's own kickoff, not from now, is what makes a past week show
/// the form each team took into that game, and keeps the answer fixed once that game is played.
/// </remarks>
public static class TeamFormCalculator
{
    /// <summary>How many recent games the tray shows per team.</summary>
    public const int GameCount = 3;

    /// <summary>
    /// The team's last <see cref="GameCount"/> games that kicked off strictly before
    /// <paramref name="beforeUtc"/>, newest first (ties on kickoff broken by game id so the order is
    /// stable).
    /// </summary>
    /// <param name="teamId">The team.</param>
    /// <param name="beforeUtc">The picked game's kickoff, UTC.</param>
    /// <param name="games">Final games of the season, for any teams.</param>
    /// <returns>Zero to <see cref="GameCount"/> entries.</returns>
    public static FormEntry[] LastGames(Guid teamId, DateTime beforeUtc, IEnumerable<FormGame> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        return
        [
            .. games
                .Where(game => (game.HomeTeamId == teamId || game.AwayTeamId == teamId) && game.KickoffUtc < beforeUtc)
                .OrderByDescending(game => game.KickoffUtc)
                .ThenBy(game => game.GameId)
                .Take(GameCount)
                .Select(game => Orient(teamId, game)),
        ];
    }

    private static FormEntry Orient(Guid teamId, FormGame game)
    {
        bool isHome = game.HomeTeamId == teamId;
        int teamScore = isHome ? game.HomeScore : game.AwayScore;
        int opponentScore = isHome ? game.AwayScore : game.HomeScore;

        FormResult result = teamScore.CompareTo(opponentScore) switch
        {
            > 0 => FormResult.Win,
            < 0 => FormResult.Loss,
            _ => FormResult.Tie,
        };

        return new FormEntry(
            game.GameId,
            isHome ? game.AwayTeamId : game.HomeTeamId,
            isHome,
            teamScore,
            opponentScore,
            result,
            game.KickoffUtc);
    }
}
