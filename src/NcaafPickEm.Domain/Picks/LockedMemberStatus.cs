using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// The verdict <see cref="WeekLocker"/> reaches for one membership. Only memberships that get a
/// <c>WeekSubmissions</c> row appear.
/// </summary>
/// <param name="MembershipId">The membership.</param>
/// <param name="Status">
/// <see cref="SubmissionStatus.Locked"/> for a member who was Submitted at lock,
/// <see cref="SubmissionStatus.Incomplete"/> for everyone else — including a member who had
/// picked nothing at all.
/// </param>
public readonly record struct LockedMemberStatus(Guid MembershipId, SubmissionStatus Status);
