namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// Code for one <see cref="PickRuleViolation"/>. <c>Api/Endpoints/PickRuleViolationResults</c>
/// maps each code to an HTTP status, and the code name is the <c>ProblemDetails</c> title
/// (03-API-Contracts.md, Picks).
/// </summary>
public enum PickRuleViolationCode
{
    /// <summary>
    /// The week exists for this league but is not the current week, so picks are neither created
    /// nor changed for it. 409. Past weeks stay readable through <c>GET .../picks/me</c>.
    /// </summary>
    WeekNotCurrent,

    /// <summary>
    /// The week is locked: the lock job has run, or the first kickoff has simply passed. 409.
    /// </summary>
    Locked,

    /// <summary>The week has no game set, or the game is not in it. 404.</summary>
    GameNotInSet,

    /// <summary>The game is in the set but removed or voided, so it takes no picks. 409.</summary>
    GameNotActive,

    /// <summary>The picked team is neither the home nor the away team of that game. 400.</summary>
    TeamNotInGame,

    /// <summary>Submit was pressed with at least one active game unpicked. 409;
    /// <see cref="PickRuleViolation.Count"/> carries how many are missing.</summary>
    IncompletePicks,

    /// <summary>Submit was pressed on a week whose set holds no active games. 409.</summary>
    NoGamesInSet,

    /// <summary>Another member's picks were requested before the week locked. 403.</summary>
    PicksNotVisible,
}

/// <summary>
/// Thrown by <c>Infrastructure/Services/PickService</c> when a pick operation cannot proceed.
/// Mapped to <c>ProblemDetails</c> by the endpoint layer, the same way
/// <see cref="GameSets.GameSetRuleViolation"/> is.
/// </summary>
public sealed class PickRuleViolation : Exception
{
    /// <summary>Creates the violation.</summary>
    /// <param name="code">Which rule was broken.</param>
    /// <param name="message">Human-readable detail for the <c>ProblemDetails</c> body.</param>
    /// <param name="count">
    /// The count that triggered <see cref="PickRuleViolationCode.IncompletePicks"/>.
    /// </param>
    public PickRuleViolation(PickRuleViolationCode code, string message, int? count = null)
        : base(message)
    {
        Code = code;
        Count = count;
    }

    /// <summary>Which rule was broken.</summary>
    public PickRuleViolationCode Code { get; }

    /// <summary>How many active games are still unpicked, for
    /// <see cref="PickRuleViolationCode.IncompletePicks"/>.</summary>
    public int? Count { get; }
}
