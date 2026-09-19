namespace NcaafPickEm.Domain.Scoring;

/// <summary>
/// Code for one <see cref="CorrectionRuleViolation"/>. <c>Api/Endpoints/CorrectionRuleViolationResults</c>
/// maps each code to an HTTP status the same way <c>GameSetRuleViolationResults</c> does
/// (05-Conventions.md: 400 for a shape problem, 404 for a resource that does not exist, 409 for a
/// request that conflicts with the row's current state).
/// </summary>
public enum CorrectionRuleViolationCode
{
    /// <summary>The named game is not part of this week's active set. 404.</summary>
    GameNotFound,

    /// <summary>The week has not locked yet; overriding or voiding a result is a post-lock
    /// action only. 409.</summary>
    NotLocked,

    /// <summary>The row has already been voided, so it cannot also carry a result override, and
    /// a second void is refused rather than silently repeated. 409.</summary>
    AlreadyVoided,

    /// <summary>The declared winner is neither the game's home nor away team. 400.</summary>
    TeamNotInGame,
}

/// <summary>
/// Thrown by <c>Infrastructure/Services/CorrectionService</c> (Feature 06, P5-02) when an
/// override or void cannot proceed. Mapped to <c>ProblemDetails</c> by the endpoint layer.
/// </summary>
public sealed class CorrectionRuleViolation : Exception
{
    /// <summary>Creates the violation.</summary>
    public CorrectionRuleViolation(CorrectionRuleViolationCode code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>Which rule was broken.</summary>
    public CorrectionRuleViolationCode Code { get; }
}
