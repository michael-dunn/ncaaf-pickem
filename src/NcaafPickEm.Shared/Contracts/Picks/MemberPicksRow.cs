using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Picks;

/// <summary>One member's whole week in the post-lock all-picks view.</summary>
/// <param name="MembershipId">The membership.</param>
/// <param name="DisplayName">Effective per-league name.</param>
/// <param name="Status">Their status for the week, as the lock job left it.</param>
/// <param name="Picks">One entry per active game, in the same order as <c>WeekPicksResponse.Games</c>.</param>
public sealed record MemberPicksRow(
    Guid MembershipId,
    string DisplayName,
    SubmissionStatus Status,
    MemberPickDto[] Picks);
