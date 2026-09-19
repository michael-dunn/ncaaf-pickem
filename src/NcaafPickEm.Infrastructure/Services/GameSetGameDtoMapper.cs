using NcaafPickEm.Domain.Points;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Reference;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// The one place a <see cref="Game"/> (plus its resolved point value and rank) becomes a
/// <see cref="GameSetGameDto"/>. Every game-bearing endpoint in P3-03 goes through this — and
/// P4-01/P5-03/P6-02 are expected to reuse it rather than re-derive <c>WinnerTeamId</c> or
/// <c>IsPointValueElevated</c> by hand.
/// </summary>
public static class GameSetGameDtoMapper
{
    /// <summary>Projects a <see cref="Team"/> entity to its DTO.</summary>
    public static TeamDto ToTeamDto(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        return new TeamDto(team.Id, team.School, team.Abbreviation, team.ConferenceId, team.LogoUrl);
    }

    /// <summary>
    /// Builds one <see cref="GameSetGameDto"/>.
    /// </summary>
    /// <param name="gameSetGameId">The persisted row's id, or null in a preview.</param>
    /// <param name="game">The underlying schedule row.</param>
    /// <param name="homeRank">AP rank for the week, when ranked.</param>
    /// <param name="awayRank">AP rank for the week, when ranked.</param>
    /// <param name="pointValue">The resolved point value.</param>
    /// <param name="leagueDefaultPointValue">The league's default, to derive
    /// <see cref="GameSetGameDto.IsPointValueElevated"/>.</param>
    /// <param name="source">Rule or Manual.</param>
    /// <param name="isVoided">Excluded from scoring after lock (Feature 06).</param>
    /// <param name="resultOverrideWinnerTeamId">Commissioner's winner correction, when set.</param>
    public static GameSetGameDto Map(
        Guid? gameSetGameId,
        Game game,
        int? homeRank,
        int? awayRank,
        int pointValue,
        int leagueDefaultPointValue,
        GameSetGameSource source,
        bool isVoided,
        Guid? resultOverrideWinnerTeamId)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(game.HomeTeam);
        ArgumentNullException.ThrowIfNull(game.AwayTeam);

        Guid? winnerTeamId = resultOverrideWinnerTeamId ?? WinnerFromScore(game);

        return new GameSetGameDto(
            gameSetGameId,
            game.Id,
            ToTeamDto(game.HomeTeam),
            ToTeamDto(game.AwayTeam),
            homeRank,
            awayRank,
            new DateTimeOffset(game.KickoffUtc, TimeSpan.Zero),
            pointValue,
            PointValueResolver.IsElevated(pointValue, leagueDefaultPointValue),
            source,
            game.Status,
            game.HomeScore,
            game.AwayScore,
            game.Period,
            game.Clock,
            isVoided,
            winnerTeamId);
    }

    private static Guid? WinnerFromScore(Game game)
    {
        if (game.Status != GameStatus.Final || game.HomeScore is null || game.AwayScore is null)
        {
            return null;
        }

        if (game.HomeScore == game.AwayScore)
        {
            return null;
        }

        return game.HomeScore > game.AwayScore ? game.HomeTeamId : game.AwayTeamId;
    }
}
