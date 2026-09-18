namespace NcaafPickEm.Shared.Contracts.GameSets;

/// <summary>Body of <c>GET /api/leagues/{leagueId}/weeks/{week}/gameset</c> and of generate.</summary>
/// <param name="Week">Week number.</param>
/// <param name="LockAtUtc">Earliest kickoff among active games; null when the set is empty.</param>
/// <param name="LockAtEasternDisplay">Eastern rendering of the lock instant, e.g. "Sat 12:00 PM ET".</param>
/// <param name="IsLocked">The lock job has run for this week.</param>
/// <param name="IsComplete">Every active game is Final or Voided.</param>
/// <param name="Games">Active games ordered by kickoff.</param>
public sealed record WeekGameSetResponse(
    int Week,
    DateTimeOffset? LockAtUtc,
    string? LockAtEasternDisplay,
    bool IsLocked,
    bool IsComplete,
    GameSetGameDto[] Games);
