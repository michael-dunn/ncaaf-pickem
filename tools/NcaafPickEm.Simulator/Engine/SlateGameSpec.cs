namespace NcaafPickEm.Simulator.Engine;

/// <summary>One game the slate builder decided on, before it is written as a <c>Games</c> row.</summary>
/// <param name="HomeTeamId">Home team.</param>
/// <param name="AwayTeamId">Away team.</param>
/// <param name="KickoffUtc">Kickoff.</param>
/// <param name="Spread">Home spread (negative = home favoured), or null for "no line".</param>
/// <param name="IsConferenceGame">Both teams in the same conference.</param>
/// <param name="IsSaturday">
/// What <c>Games.IsSaturdayEastern</c> is stored as. True for every regular slate game whatever
/// day it really is (Saturday is a flag in the simulator, D-185); false for the off-day game.
/// </param>
public sealed record SlateGameSpec(
    Guid HomeTeamId,
    Guid AwayTeamId,
    DateTimeOffset KickoffUtc,
    decimal? Spread,
    bool IsConferenceGame,
    bool IsSaturday);
