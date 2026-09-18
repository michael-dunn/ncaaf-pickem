using System.Linq.Expressions;

namespace NcaafPickEm.Domain.Leagues;

/// <summary>
/// The one place that decides a member's effective per-league name: the per-league override when
/// set, else the account-wide name (Feature 01 acceptance criteria; Feature 08 display names).
/// Every DTO that carries a member name (<c>MemberRow</c>, invite previews, future dashboard and
/// leaderboard rows) projects through <see cref="Selector"/> so the rule can never drift between
/// endpoints.
/// </summary>
public static class MemberNameProjection
{
    /// <summary>
    /// An EF-translatable expression: <c>m.DisplayNameOverride ?? m.User.DisplayName</c>. Use this
    /// directly in a LINQ <c>Select</c> against <c>AppDbContext.Memberships</c> so the database
    /// computes the effective name instead of the app loading every user row.
    /// </summary>
    public static Expression<Func<Membership, string>> Selector { get; } =
        membership => membership.DisplayNameOverride ?? (membership.User != null ? membership.User.DisplayName : string.Empty);

    /// <summary>
    /// The same rule, evaluated in memory against an already-loaded <see cref="Membership"/>
    /// (its <see cref="Membership.User"/> navigation must be loaded).
    /// </summary>
    public static string Effective(Membership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);
        return membership.DisplayNameOverride ?? membership.User?.DisplayName ?? string.Empty;
    }
}
