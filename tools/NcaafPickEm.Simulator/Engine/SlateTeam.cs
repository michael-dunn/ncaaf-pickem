namespace NcaafPickEm.Simulator.Engine;

/// <summary>A team as the slate builder sees it: identity, conference, and whether it is FBS.</summary>
/// <param name="TeamId">The <c>Teams.Id</c> row.</param>
/// <param name="School">School name, for notes.</param>
/// <param name="Conference">Conference abbreviation, or null (FCS).</param>
/// <param name="IsFbs">False for the FCS teams.</param>
public sealed record SlateTeam(Guid TeamId, string School, string? Conference, bool IsFbs)
{
    /// <summary>FBS independents share a "conference" in the catalog but never play conference games.</summary>
    public const string Independents = "Ind";

    /// <summary>True when both teams are in the same real conference.</summary>
    /// <param name="other">The opponent.</param>
    public bool SharesConferenceWith(SlateTeam other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Conference is not null
            && Conference != Independents
            && string.Equals(Conference, other.Conference, StringComparison.Ordinal);
    }
}
