using System.Globalization;
using NcaafPickEm.Shared.Contracts.Reference;

namespace NcaafPickEm.Shared.Contracts.GameSets;

/// <summary>
/// Renders <see cref="GameSetGameDto.Spread"/> the way a sportsbook does: the favored team's
/// abbreviation and the points it is giving, e.g. <c>MICH -7.5</c>. Lives beside the DTO so
/// every page that shows a line spells it the same way.
/// </summary>
public static class SpreadDisplay
{
    /// <summary>Shown when the line is exactly zero.</summary>
    public const string PickEm = "Pick 'em";

    /// <summary>
    /// Formats a home-relative spread against the two teams, or returns null when there is no line.
    /// </summary>
    /// <param name="spread">Home minus away; negative means the home team is favored.</param>
    /// <param name="homeTeam">The home team.</param>
    /// <param name="awayTeam">The away team.</param>
    public static string? Format(decimal? spread, TeamDto homeTeam, TeamDto awayTeam)
    {
        ArgumentNullException.ThrowIfNull(homeTeam);
        ArgumentNullException.ThrowIfNull(awayTeam);

        if (spread is not { } value)
        {
            return null;
        }

        if (value == 0m)
        {
            return PickEm;
        }

        TeamDto favorite = value < 0m ? homeTeam : awayTeam;
        decimal giving = -Math.Abs(value);

        return string.Create(CultureInfo.InvariantCulture, $"{Name(favorite)} {giving:0.#}");
    }

    /// <summary>The short name to lead with; a team with no abbreviation falls back to its school, as the server's TeamDto mapping does.</summary>
    private static string Name(TeamDto team) =>
        string.IsNullOrWhiteSpace(team.Abbreviation) ? team.School : team.Abbreviation;
}
