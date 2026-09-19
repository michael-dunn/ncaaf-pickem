namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// Code for one <see cref="GameSetRuleViolation"/>. <c>Api/Endpoints/GameSetRuleViolationResults</c>
/// maps each code to an HTTP status (05-Conventions.md: 400/404 for shape or nonexistent-resource
/// problems, 409 for a request that conflicts with the week's current state).
/// </summary>
public enum GameSetRuleViolationCode
{
    /// <summary>The requested week is outside the league's First..Last range. 404.</summary>
    WeekOutOfRange,

    /// <summary>The week is already locked; generation, manual add/remove, and point overrides
    /// are all refused from then on. 409.</summary>
    Locked,

    /// <summary>The configuration would produce more than <see cref="GameSetLimits.MaxGames"/>
    /// games. 409; <see cref="GameSetRuleViolation.Count"/> carries how many.</summary>
    ExceedsMax,

    /// <summary>A manual add's game is not Saturday-Eastern, not both-FBS, or is postponed or
    /// cancelled. 409.</summary>
    GameNotEligible,

    /// <summary>The named game is not part of this week's schedule at all. 404.</summary>
    GameNotFound,
}

/// <summary>
/// Thrown by <c>Infrastructure/Services/GameSetService</c> and <c>PointRuleService</c> when a
/// game-set or point-value operation cannot proceed. Mapped to <c>ProblemDetails</c> by the
/// endpoint layer.
/// </summary>
public sealed class GameSetRuleViolation : Exception
{
    /// <summary>Creates the violation.</summary>
    public GameSetRuleViolation(GameSetRuleViolationCode code, string message, int? count = null)
        : base(message)
    {
        Code = code;
        Count = count;
    }

    /// <summary>Which rule was broken.</summary>
    public GameSetRuleViolationCode Code { get; }

    /// <summary>The game count that triggered <see cref="GameSetRuleViolationCode.ExceedsMax"/>.</summary>
    public int? Count { get; }
}
