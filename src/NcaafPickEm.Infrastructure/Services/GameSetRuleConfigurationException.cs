namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Thrown by <see cref="GameSetService"/> when a submitted <c>GameSetRuleDto[]</c> (default or
/// week override) fails shape validation: a conference rule with no FBS conference, a team rule
/// with no FBS team, or an unknown rule type. Mapped to a 400 <c>ValidationProblem</c> by the
/// endpoint layer, keyed the same way <c>ValidationFilter&lt;T&gt;</c> keys FluentValidation
/// failures.
/// </summary>
public sealed class GameSetRuleConfigurationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public GameSetRuleConfigurationException(Dictionary<string, string[]> errors)
        : base("One or more game-set rules are invalid.")
    {
        Errors = errors;
    }

    /// <summary>Field name (e.g. <c>Rules[0].ConferenceId</c>) to messages.</summary>
    public Dictionary<string, string[]> Errors { get; }
}
