namespace NcaafPickEm.Shared.Contracts.Stats;

/// <summary>One row of a member profile's per-week table.</summary>
/// <param name="Week">The week; the client links it to that week's leaderboard.</param>
/// <param name="Correct">Correct picks.</param>
/// <param name="Total">Active (non-voided) games in the set.</param>
/// <param name="Points">Points earned.</param>
/// <param name="Rank">Competition rank by points among that week's result rows.</param>
/// <param name="IsComplete">False while the week is still being played (provisional).</param>
public sealed record MemberWeekStat(
    int Week,
    int Correct,
    int Total,
    int Points,
    int Rank,
    bool IsComplete);
