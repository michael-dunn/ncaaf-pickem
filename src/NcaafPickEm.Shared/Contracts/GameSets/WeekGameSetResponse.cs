namespace NcaafPickEm.Shared.Contracts.GameSets;

/// <summary>Body of <c>GET /api/leagues/{leagueId}/weeks/{week}/gameset</c> and of generate.</summary>
/// <param name="Week">Week number.</param>
/// <param name="LockAtUtc">Earliest kickoff among active games; null when the set is empty.</param>
/// <param name="LockAtEasternDisplay">Eastern rendering of the lock instant, e.g. "Sat 12:00 PM ET".</param>
/// <param name="IsLocked">The lock job has run for this week.</param>
/// <param name="IsComplete">Every active game is Final or Voided.</param>
/// <param name="Games">
/// Active games ordered by kickoff. Empty for a member (not a commissioner) while
/// <see cref="PicksOpen"/> is false: the provisional set is the commissioner's preview (Feature 16).
/// </param>
/// <param name="PicksOpenAtUtc">When the week's picks open; null when no season calendar is known.</param>
/// <param name="PicksOpenAtEasternDisplay">Eastern rendering of <see cref="PicksOpenAtUtc"/>.</param>
/// <param name="PicksOpen">True once the picks-open moment has passed and the set has games.</param>
public sealed record WeekGameSetResponse(
    int Week,
    DateTimeOffset? LockAtUtc,
    string? LockAtEasternDisplay,
    bool IsLocked,
    bool IsComplete,
    GameSetGameDto[] Games,
    DateTimeOffset? PicksOpenAtUtc = null,
    string? PicksOpenAtEasternDisplay = null,
    bool PicksOpen = true);
