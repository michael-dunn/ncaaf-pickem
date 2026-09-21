using NcaafPickEm.Domain.Points;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Reference;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// The one place a <see cref="Game"/> (plus its resolved point value and rank) becomes a
/// <see cref="GameSetGameDto"/>. Every game-bearing endpoint in P3-03 goes through this — and
/// P4-01/P5-03/P6-02 are expected to reuse it rather than re-derive <c>WinnerTeamId</c> or
/// <c>IsPointValueElevated</c> by hand. The winner itself comes from
/// <see cref="WinnerResolver"/>, the one place that rule lives.
/// </summary>
public static class GameSetGameDtoMapper
{
    /// <summary>
    /// Projects a <see cref="Team"/> entity to its DTO. A team with no abbreviation - CFBD
    /// leaves it blank for a handful of the 682 schools it reports - falls back to its school
    /// name, so no UI ever renders an empty chip (D-171).
    /// </summary>
    public static TeamDto ToTeamDto(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        string abbreviation = string.IsNullOrWhiteSpace(team.Abbreviation) ? team.School : team.Abbreviation;

        return new TeamDto(team.Id, team.School, abbreviation, team.ConferenceId, team.LogoUrl);
    }

    /// <summary>
    /// Projects a <see cref="Conference"/> entity to its DTO, with the same empty-abbreviation
    /// fallback <see cref="ToTeamDto"/> uses (D-171).
    /// </summary>
    public static ConferenceDto ToConferenceDto(Conference conference)
    {
        ArgumentNullException.ThrowIfNull(conference);

        string abbreviation = string.IsNullOrWhiteSpace(conference.Abbreviation)
            ? conference.Name
            : conference.Abbreviation;

        return new ConferenceDto(conference.Id, conference.Name, abbreviation);
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
    /// <param name="spread">Home-relative spread to show (newest line, or the frozen
    /// <c>SpreadAtLock</c> once locked); null when the game has no line.</param>
    public static GameSetGameDto Map(
        Guid? gameSetGameId,
        Game game,
        int? homeRank,
        int? awayRank,
        int pointValue,
        int leagueDefaultPointValue,
        GameSetGameSource source,
        bool isVoided,
        Guid? resultOverrideWinnerTeamId,
        decimal? spread)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(game.HomeTeam);
        ArgumentNullException.ThrowIfNull(game.AwayTeam);

        Guid? winnerTeamId = WinnerResolver.Resolve(
            resultOverrideWinnerTeamId,
            game.Status,
            game.HomeScore,
            game.AwayScore,
            game.HomeTeamId,
            game.AwayTeamId);

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
            winnerTeamId,
            spread);
    }
}
