namespace NcaafPickEm.Domain.Dashboard;

/// <summary>
/// One member's pick on one game in the locked set. Flattened from the <c>Picks</c> row.
/// </summary>
/// <param name="MembershipId">Who picked. A pick whose membership is not in
/// <see cref="InfluenceRequest.MembersActiveAtLock"/> is ignored outright.</param>
/// <param name="GameSetGameId">Which <c>WeekGameSetGames</c> row. A pick on a game that is not in
/// <see cref="InfluenceRequest.Games"/> - removed before lock, or voided after it - is ignored.</param>
/// <param name="TeamId">The team picked; the home or away team of that game.</param>
public sealed record InfluencePick(Guid MembershipId, Guid GameSetGameId, Guid TeamId);
