using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Picks;

/// <summary>One row of the commissioner's "who has submitted" roster for a week.</summary>
/// <param name="MembershipId">The membership.</param>
/// <param name="DisplayName">Effective per-league name.</param>
/// <param name="Status">Their status for the week; NotStarted when they have never touched it.</param>
/// <param name="PickedCount">Picks they hold on active games.</param>
/// <param name="TotalCount">Active games in the week's set.</param>
public sealed record MemberStatusRow(
    Guid MembershipId,
    string DisplayName,
    SubmissionStatus Status,
    int PickedCount,
    int TotalCount);
