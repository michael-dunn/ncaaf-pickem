namespace NcaafPickEm.Shared.Contracts.Admin;

/// <summary>
/// A Final game with no determinable winner (a tie, or a missing score) inside an active league
/// set: Feature 06's "needs review" path (04-Domain-Algorithms.md sections 7 and 12), surfaced
/// so a commissioner can override the result or void the game once Phase 5 adds those actions.
/// </summary>
/// <param name="GameId">The game.</param>
/// <param name="LeagueId">The league whose set it is in, for the link to that league's week.</param>
/// <param name="LeagueName">The league's name, for display without a second lookup.</param>
/// <param name="Week">The provider week.</param>
/// <param name="HomeTeam">Home school name.</param>
/// <param name="AwayTeam">Away school name.</param>
/// <param name="HomeScore">Final home score, or null if the feed never reported one.</param>
/// <param name="AwayScore">Final away score, or null if the feed never reported one.</param>
/// <param name="Reason">Why review is needed, e.g. "Tie" or "Missing score".</param>
public sealed record NeedsReviewGameDto(
    Guid GameId,
    Guid LeagueId,
    string LeagueName,
    int Week,
    string HomeTeam,
    string AwayTeam,
    int? HomeScore,
    int? AwayScore,
    string Reason);
