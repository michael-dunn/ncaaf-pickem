namespace NcaafPickEm.Shared.Contracts.Dev;

/// <summary>One demo-league member's week, as <see cref="DemoWeekResponse"/> reports it.</summary>
/// <param name="Name">Display name (the membership override when set, else the user's).</param>
/// <param name="Role">"Commissioner" or "Member".</param>
/// <param name="Status">
/// The member's submission status for the week: "NotStarted", "InProgress", "Submitted",
/// "Locked" or "Incomplete".
/// </param>
/// <param name="PickedCount">Picks the member holds on games still in the set.</param>
/// <param name="Points">Points scored this week, or null before the week has been scored.</param>
/// <param name="CorrectCount">Correct picks this week, or null before the week has been scored.</param>
public sealed record DemoMemberDto(
    string Name,
    string Role,
    string Status,
    int PickedCount,
    int? Points,
    int? CorrectCount);
