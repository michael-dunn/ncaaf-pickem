using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// One candidate game, flattened for the generator. Deliberately not the <c>Games</c> entity: the
/// generator is a pure function over in-memory values, so the application service projects
/// entities (and their teams' conference and classification) into these before calling it.
/// </summary>
/// <param name="GameId">The <c>Games</c> row id.</param>
/// <param name="HomeTeamId">Home team id.</param>
/// <param name="AwayTeamId">Away team id.</param>
/// <param name="HomeConferenceId">Home team's conference, null when the team has none.</param>
/// <param name="AwayConferenceId">Away team's conference, null when the team has none.</param>
/// <param name="HomeClassification">Home team's NCAA division.</param>
/// <param name="AwayClassification">Away team's NCAA division.</param>
/// <param name="KickoffUtc">Scheduled kickoff in UTC.</param>
/// <param name="IsSaturdayEastern">
/// Copied from <c>Games.IsSaturdayEastern</c>, which is computed at ingest. A Friday 9:30 PM
/// Pacific kickoff is Saturday 12:30 AM Eastern and is therefore true; a Friday 7:00 PM Eastern
/// kickoff is false. The generator never converts time zones itself.
/// </param>
/// <param name="IsConferenceGame">
/// The provider's conference-game flag. Carried so that callers can project once and hand the
/// same values to the point value resolver; rule selection does not use it.
/// </param>
/// <param name="Status">Current game status.</param>
public sealed record GameInfo(
    Guid GameId,
    Guid HomeTeamId,
    Guid AwayTeamId,
    Guid? HomeConferenceId,
    Guid? AwayConferenceId,
    TeamClassification HomeClassification,
    TeamClassification AwayClassification,
    DateTime KickoffUtc,
    bool IsSaturdayEastern,
    bool IsConferenceGame,
    GameStatus Status);
