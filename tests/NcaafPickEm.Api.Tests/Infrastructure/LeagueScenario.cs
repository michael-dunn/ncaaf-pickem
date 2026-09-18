namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// A seeded league and the three callers an authorization matrix needs.
/// </summary>
/// <param name="LeagueId">The league.</param>
/// <param name="CommissionerUserId">Active membership with role Commissioner.</param>
/// <param name="MemberUserId">Active membership with role Member.</param>
/// <param name="StrangerUserId">A real user with no membership in this league.</param>
public sealed record LeagueScenario(
    Guid LeagueId,
    Guid CommissionerUserId,
    Guid MemberUserId,
    Guid StrangerUserId);
