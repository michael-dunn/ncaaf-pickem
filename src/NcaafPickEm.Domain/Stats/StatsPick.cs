namespace NcaafPickEm.Domain.Stats;

/// <summary>One member's pick on one game in a locked set, flattened for <see cref="MemberStatsCalculator"/>.</summary>
/// <param name="MembershipId">Who picked.</param>
/// <param name="GameSetGameId">The <c>WeekGameSetGames.Id</c> picked on.</param>
/// <param name="TeamId">The team picked.</param>
public sealed record StatsPick(
    Guid MembershipId,
    Guid GameSetGameId,
    Guid TeamId);
