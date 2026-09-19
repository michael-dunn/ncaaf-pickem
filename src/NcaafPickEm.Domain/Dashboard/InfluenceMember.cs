namespace NcaafPickEm.Domain.Dashboard;

/// <summary>
/// One member the dashboard can name in an "opposite picks" or "no pick" list. The same instances
/// the caller passes in come back out in the result, so the endpoint can carry whatever extra
/// identity it needs alongside these two fields.
/// </summary>
/// <param name="MembershipId">The membership. Picks and the viewer are matched on this.</param>
/// <param name="DisplayName">The name to show - the league nickname when set, else the global
/// display name. Resolving that is the caller's job; the calculator never inspects it.</param>
/// <param name="IsFormer">True when the member has since left the league. Former members who were
/// active at lock still appear on everybody's dashboard, flagged, because their picks still
/// count - see <see cref="InfluenceRequest.MembersActiveAtLock"/>.</param>
public sealed record InfluenceMember(Guid MembershipId, string DisplayName, bool IsFormer);
