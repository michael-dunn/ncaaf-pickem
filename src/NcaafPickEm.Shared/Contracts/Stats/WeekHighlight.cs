namespace NcaafPickEm.Shared.Contracts.Stats;

/// <summary>A member's best or worst Complete week by one measure (e.g. "Week 6: 11/12").</summary>
/// <param name="Week">The week; the most recent one when several tie.</param>
/// <param name="Value">Correct picks or points that week.</param>
/// <param name="GameCount">Active games in that week's set.</param>
/// <param name="OtherTiedWeeks">The other weeks with the same value, most recent first; the
/// client shows them as "+N more".</param>
public sealed record WeekHighlight(
    int Week,
    int Value,
    int GameCount,
    int[] OtherTiedWeeks);
