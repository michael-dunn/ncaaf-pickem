namespace NcaafPickEm.Shared.Contracts.Stats;

/// <summary>One stat category on a member's profile.</summary>
/// <param name="Rank">The member's rank in the category on the league stats page; null for a
/// former member, who is not ranked.</param>
/// <param name="RankedCount">How many members the category ranks.</param>
/// <param name="Hits">The numerator.</param>
/// <param name="Total">The denominator.</param>
public sealed record MemberStat(
    int? Rank,
    int RankedCount,
    int Hits,
    int Total);
