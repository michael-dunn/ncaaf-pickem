namespace NcaafPickEm.Domain.TeamForm;

/// <summary>
/// One Final game with both scores known, flattened for <see cref="TeamFormCalculator"/>. The
/// caller decides "Final" and "this season"; the calculator only picks and orients.
/// </summary>
/// <param name="GameId">The <c>Games.Id</c>.</param>
/// <param name="HomeTeamId">Home team.</param>
/// <param name="AwayTeamId">Away team.</param>
/// <param name="KickoffUtc">Kickoff, UTC.</param>
/// <param name="HomeScore">Home team's final score.</param>
/// <param name="AwayScore">Away team's final score.</param>
public sealed record FormGame(
    Guid GameId,
    Guid HomeTeamId,
    Guid AwayTeamId,
    DateTime KickoffUtc,
    int HomeScore,
    int AwayScore);
