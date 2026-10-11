namespace NcaafPickEm.Shared.Contracts.Stats;

/// <summary>One member's line in a stat category on the league stats page.</summary>
/// <param name="Rank">1-based competition rank by percentage; equal fractions share a rank.</param>
/// <param name="MembershipId">The membership; the client links it to the member's profile.</param>
/// <param name="DisplayName">Effective per-league name.</param>
/// <param name="Hits">The numerator: correct picks, or underdog wins picked.</param>
/// <param name="Total">The denominator: counted games, or underdog wins, in the member's weeks.
/// Zero reads as 0% and ranks with the other zeroes.</param>
/// <param name="IsMe">True for the caller's row.</param>
public sealed record StatRow(
    int Rank,
    Guid MembershipId,
    string DisplayName,
    int Hits,
    int Total,
    bool IsMe);
