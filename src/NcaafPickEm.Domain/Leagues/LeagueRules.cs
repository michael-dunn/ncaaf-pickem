using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Leagues;

/// <summary>
/// Pure invariant checks for leagues and memberships (Feature 01, P1-01). No I/O, no clock: every
/// check takes the state it needs as an argument and either returns a normalized value or throws
/// <see cref="LeagueRuleViolation"/>. Application services own persistence and audit logging.
/// </summary>
public static class LeagueRules
{
    /// <summary>Maximum active members in a league (Feature 01 acceptance criteria).</summary>
    public const int MemberCap = 50;

    /// <summary>
    /// Trims <paramref name="name"/> and checks it is 1..<see cref="League.NameMaxLength"/>
    /// characters.
    /// </summary>
    /// <exception cref="LeagueRuleViolation">The trimmed name is empty or too long.</exception>
    public static string ValidateName(string? name)
    {
        string trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length is 0 or > League.NameMaxLength)
        {
            throw new LeagueRuleViolation(
                LeagueRuleViolationCode.InvalidName,
                $"League name must be 1 to {League.NameMaxLength} characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// Checks that <paramref name="firstWeek"/>..<paramref name="lastWeek"/> are both
    /// regular-season weeks of <paramref name="seasonWeeks"/> and in order.
    /// </summary>
    /// <exception cref="LeagueRuleViolation">The range is invalid.</exception>
    public static void ValidateWeekRange(int firstWeek, int lastWeek, IReadOnlyList<SeasonWeek> seasonWeeks)
    {
        ArgumentNullException.ThrowIfNull(seasonWeeks);

        HashSet<int> regularWeeks = [.. seasonWeeks.Where(week => week.IsRegularSeason).Select(week => week.Week)];

        if (firstWeek > lastWeek || !regularWeeks.Contains(firstWeek) || !regularWeeks.Contains(lastWeek))
        {
            throw new LeagueRuleViolation(
                LeagueRuleViolationCode.InvalidWeekRange,
                "First and last week must be regular-season weeks, with first not after last.");
        }
    }

    /// <summary>Checks the league has room for one more active member.</summary>
    /// <exception cref="LeagueRuleViolation">The league is already at <see cref="MemberCap"/>.</exception>
    public static void EnsureRoomForNewMember(int activeMemberCount)
    {
        if (activeMemberCount >= MemberCap)
        {
            throw new LeagueRuleViolation(
                LeagueRuleViolationCode.LeagueFull,
                $"The league is at its {MemberCap}-member cap.");
        }
    }

    /// <summary>
    /// Checks a commissioner is not acting on their own membership (remove or demote).
    /// </summary>
    /// <exception cref="LeagueRuleViolation">
    /// <paramref name="actingMembershipId"/> equals <paramref name="targetMembershipId"/>.
    /// </exception>
    public static void EnsureNotActingOnSelf(Guid actingMembershipId, Guid targetMembershipId, string action)
    {
        if (actingMembershipId == targetMembershipId)
        {
            throw new LeagueRuleViolation(
                LeagueRuleViolationCode.CannotActOnSelf,
                $"A commissioner cannot {action} themselves; transfer the role first.");
        }
    }

    /// <summary>
    /// Checks that removing/demoting a commissioner would not leave the league with zero active
    /// commissioners. Pass the count of active commissioners <em>excluding</em> the one being
    /// acted on.
    /// </summary>
    /// <exception cref="LeagueRuleViolation">
    /// <paramref name="remainingActiveCommissionerCount"/> is zero.
    /// </exception>
    public static void EnsureAtLeastOneCommissionerRemains(int remainingActiveCommissionerCount)
    {
        if (remainingActiveCommissionerCount < 1)
        {
            throw new LeagueRuleViolation(
                LeagueRuleViolationCode.LastCommissioner,
                "A league must always have at least one active commissioner.");
        }
    }

    /// <summary>
    /// Applies commissioner-role transfer semantics: <paramref name="target"/> is promoted,
    /// <paramref name="caller"/> is demoted. Every other commissioner is untouched because
    /// nothing else is mutated.
    /// </summary>
    /// <exception cref="LeagueRuleViolation">
    /// The target is the caller, not in the same league, or not an active member.
    /// </exception>
    public static void ApplyTransfer(Membership caller, Membership target)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(target);

        if (caller.Id == target.Id)
        {
            throw new LeagueRuleViolation(
                LeagueRuleViolationCode.InvalidTransferTarget,
                "Cannot transfer the commissioner role to yourself.");
        }

        if (target.LeagueId != caller.LeagueId || !target.IsActive)
        {
            throw new LeagueRuleViolation(
                LeagueRuleViolationCode.InvalidTransferTarget,
                "The transfer target must be an active member of this league.");
        }

        target.Role = MembershipRole.Commissioner;
        caller.Role = MembershipRole.Member;
    }

    /// <summary>
    /// Trims <paramref name="displayName"/> and checks it is 1..
    /// <see cref="Membership.DisplayNameMaxLength"/> characters, or returns null when the input is
    /// null/empty (which clears the override).
    /// </summary>
    /// <exception cref="LeagueRuleViolation">The trimmed name is too long.</exception>
    public static string? ValidateDisplayNameOverride(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        string trimmed = displayName.Trim();
        if (trimmed.Length > Membership.DisplayNameMaxLength)
        {
            throw new LeagueRuleViolation(
                LeagueRuleViolationCode.InvalidName,
                $"Display name must be at most {Membership.DisplayNameMaxLength} characters.");
        }

        return trimmed;
    }
}
