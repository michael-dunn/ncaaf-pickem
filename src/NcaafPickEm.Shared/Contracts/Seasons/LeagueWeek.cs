namespace NcaafPickEm.Shared.Contracts.Seasons;

/// <summary>
/// One row of <c>GET /api/leagues/{leagueId}/weeks</c>, restricted to the league's First..Last range.
/// </summary>
/// <param name="Week">Week number.</param>
/// <param name="HasGameSet">A game set has been generated for this week.</param>
/// <param name="IsCurrent">The season calendar's current week.</param>
/// <param name="IsLocked">The week's game set is locked.</param>
/// <param name="IsComplete">Every active game in the set is final or voided.</param>
/// <param name="LockAtUtc">Lock instant, when a game set exists.</param>
public sealed record LeagueWeek(
    int Week,
    bool HasGameSet,
    bool IsCurrent,
    bool IsLocked,
    bool IsComplete,
    DateTimeOffset? LockAtUtc);
