namespace NcaafPickEm.Domain.Leagues;

/// <summary>
/// Code for one <see cref="LeagueRuleViolation"/>. The service layer maps each code to an HTTP
/// status (05-Conventions.md: 400 for shape problems, 409 for state conflicts).
/// </summary>
public enum LeagueRuleViolationCode
{
    /// <summary>League name is empty or longer than <see cref="League.NameMaxLength"/>. 400.</summary>
    InvalidName,

    /// <summary><c>FirstWeek</c>/<c>LastWeek</c> is not a regular-season week, or First &gt; Last. 400.</summary>
    InvalidWeekRange,

    /// <summary>The league already has <see cref="LeagueRules.MemberCap"/> active members. 409.</summary>
    LeagueFull,

    /// <summary>A commissioner tried to remove or demote themselves without transferring first. 409.</summary>
    CannotActOnSelf,

    /// <summary>The action would leave the league with zero active commissioners. 409.</summary>
    LastCommissioner,

    /// <summary>A transfer target is not an active member of the league, or is the caller. 400.</summary>
    InvalidTransferTarget,

    /// <summary>The requested per-league display name is already taken by another active member. 409.</summary>
    DisplayNameTaken,
}

/// <summary>
/// Thrown by <see cref="LeagueRules"/> when a league invariant would be broken. Pure and
/// deterministic: no I/O, no clock (05-Conventions.md). Caught by
/// <c>Infrastructure/Services/LeagueService</c> and mapped to <c>ProblemDetails</c>.
/// </summary>
public sealed class LeagueRuleViolation : Exception
{
    /// <summary>Creates the violation.</summary>
    public LeagueRuleViolation(LeagueRuleViolationCode code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>Which rule was broken.</summary>
    public LeagueRuleViolationCode Code { get; }
}
