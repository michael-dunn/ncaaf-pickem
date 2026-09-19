using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Picks;

/// <summary>
/// Body of <c>GET /api/leagues/{leagueId}/weeks/{week}/picks/me</c> and of every mutation on it
/// (set pick, submit), so one round trip always leaves the client with the whole page state.
/// </summary>
/// <param name="Week">Week number.</param>
/// <param name="Status">The caller's status for the week.</param>
/// <param name="LockAtUtc">Earliest kickoff among active games; null when the set is empty.</param>
/// <param name="LockAtEasternDisplay">Eastern rendering of the lock instant, e.g. "Sat 12:00 PM ET".</param>
/// <param name="IsLocked">
/// True when picks are frozen <em>now</em>: the lock job has run, or <see cref="LockAtUtc"/> has
/// simply passed. Broader than <c>WeekGameSetResponse.IsLocked</c>, which reports only the job
/// (D-084), because the picks page must go read-only the moment the server stops accepting picks.
/// </param>
/// <param name="PickedCount">Picks the caller holds on active games.</param>
/// <param name="TotalCount">Active games in the week's set.</param>
/// <param name="HasUnseenGameChanges">
/// True when games were added or removed since the caller last acknowledged the change; cleared by
/// <c>POST .../picks/me/ack-changes</c>.
/// </param>
/// <param name="Games">
/// Every game in the set that has not been removed, ordered by kickoff. A voided game is still
/// listed (flagged on <c>Game.IsVoided</c>) but counts towards neither <see cref="PickedCount"/>
/// nor <see cref="TotalCount"/>.
/// </param>
public sealed record MyPicksResponse(
    int Week,
    SubmissionStatus Status,
    DateTimeOffset? LockAtUtc,
    string? LockAtEasternDisplay,
    bool IsLocked,
    int PickedCount,
    int TotalCount,
    bool HasUnseenGameChanges,
    MyPickGameDto[] Games);
