using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Simulator.Engine.Views;

/// <summary>A game of the current week.</summary>
/// <param name="GameId">The <c>Games.Id</c>.</param>
/// <param name="KickoffUtc">Kickoff.</param>
/// <param name="Home">Home team.</param>
/// <param name="Away">Away team.</param>
/// <param name="Spread">Newest home spread, or null.</param>
/// <param name="IsConferenceGame">Stored conference-game flag.</param>
/// <param name="IsSaturday">Stored Saturday flag.</param>
/// <param name="Status">Current status.</param>
/// <param name="HomeScore">Home points.</param>
/// <param name="AwayScore">Away points.</param>
/// <param name="Period">Quarter while live.</param>
/// <param name="Clock">Clock while live.</param>
/// <param name="Plan">The autoplay plan, or null before one exists.</param>
/// <param name="SetCount">How many leagues' sets hold it (not removed).</param>
public sealed record SimGameView(
    Guid GameId,
    DateTimeOffset KickoffUtc,
    SimTeamView Home,
    SimTeamView Away,
    decimal? Spread,
    bool IsConferenceGame,
    bool IsSaturday,
    GameStatus Status,
    int? HomeScore,
    int? AwayScore,
    byte? Period,
    string? Clock,
    GamePlan? Plan,
    int SetCount)
{
    /// <summary>What set generation may take: Saturday, FBS against FBS, not postponed or cancelled.</summary>
    public bool IsEligible =>
        IsSaturday && Home.IsFbs && Away.IsFbs && Status is not (GameStatus.Postponed or GameStatus.Cancelled);

    /// <summary>"AWAY at HOME".</summary>
    public string Matchup => $"{Away.Label} at {Home.Label}";
}
