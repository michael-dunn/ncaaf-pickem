namespace NcaafPickEm.Simulator.Engine.Views;

/// <summary>A team in a game row.</summary>
/// <param name="TeamId">The <c>Teams.Id</c>.</param>
/// <param name="School">School name.</param>
/// <param name="Abbreviation">Short name.</param>
/// <param name="Rank">This week's AP rank, or null.</param>
/// <param name="Conference">Conference abbreviation, or null.</param>
/// <param name="IsFbs">False for FCS.</param>
public sealed record SimTeamView(Guid TeamId, string School, string? Abbreviation, int? Rank, string? Conference, bool IsFbs)
{
    /// <summary>"#3 Georgia", or just the school.</summary>
    public string Label => Rank is int rank ? $"#{rank} {School}" : School;

    /// <summary>The abbreviation, or the school when there is none.</summary>
    public string Short => string.IsNullOrWhiteSpace(Abbreviation) ? School : Abbreviation;
}
