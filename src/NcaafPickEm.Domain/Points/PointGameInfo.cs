namespace NcaafPickEm.Domain.Points;

/// <summary>
/// The only facts about a game that point resolution needs (Feature 03). A slim input record
/// rather than the <c>Game</c> entity keeps <see cref="PointValueResolver"/> pure and lets a
/// commissioner's unsaved preview resolve through exactly the same code as a saved week.
/// </summary>
/// <param name="HomeTeamId">Home team.</param>
/// <param name="AwayTeamId">Away team.</param>
/// <param name="HomeConferenceId">Home team's conference, or null when unknown.</param>
/// <param name="AwayConferenceId">Away team's conference, or null when unknown.</param>
/// <param name="IsConferenceGame">The provider's conference-game flag. A game that is not flagged
/// is never a conference game, whatever the two conference ids say.</param>
public readonly record struct PointGameInfo(
    Guid HomeTeamId,
    Guid AwayTeamId,
    Guid? HomeConferenceId,
    Guid? AwayConferenceId,
    bool IsConferenceGame);
