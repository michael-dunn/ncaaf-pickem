namespace NcaafPickEm.Web.Components;

/// <summary>
/// Correct/incorrect coloring for a selected <see cref="TeamPickButton"/>, derived by the caller
/// from <c>GameSetGameDto.WinnerTeamId</c> (P4-03). UI-only; there is no server DTO for this.
/// </summary>
public enum PickButtonOutcome
{
    /// <summary>No result yet, or this button is not the picked team.</summary>
    None,

    /// <summary>The picked team is <c>WinnerTeamId</c>.</summary>
    Correct,

    /// <summary>The game has a winner and it is not the picked team.</summary>
    Incorrect,
}
