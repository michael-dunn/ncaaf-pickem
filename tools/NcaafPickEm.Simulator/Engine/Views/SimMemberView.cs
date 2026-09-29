using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Simulator.Engine.Views;

/// <summary>A member of a simulated league.</summary>
/// <param name="MembershipId">The membership.</param>
/// <param name="UserId">The user.</param>
/// <param name="Name">Display name in the league.</param>
/// <param name="LoginName">
/// What <c>/auth/dev-login?user=</c> takes, or null for a user dev-login cannot sign in as.
/// </param>
/// <param name="Role">Commissioner or member.</param>
/// <param name="IsTester">This is "you".</param>
/// <param name="Status">This week's submission status.</param>
/// <param name="Picks">This week's picks: <c>Games.Id</c> to the picked team.</param>
/// <param name="WeekPoints">This week's points once scored.</param>
/// <param name="WeekCorrect">This week's correct picks once scored.</param>
/// <param name="SeasonPoints">Points across every scored week.</param>
public sealed record SimMemberView(
    Guid MembershipId,
    Guid UserId,
    string Name,
    string? LoginName,
    MembershipRole Role,
    bool IsTester,
    SubmissionStatus Status,
    IReadOnlyDictionary<Guid, Guid> Picks,
    int? WeekPoints,
    int? WeekCorrect,
    int SeasonPoints);
