namespace NcaafPickEm.Simulator.Catalog;

/// <summary>One team in the simulator's catalog.</summary>
/// <param name="Id">CFBD (and ESPN) team id.</param>
/// <param name="School">School name, as the app displays it.</param>
/// <param name="Mascot">Mascot, or null.</param>
/// <param name="Abbreviation">Short name for spreads and chips, e.g. <c>MICH</c>.</param>
/// <param name="Conference">The conference abbreviation, or null for an FCS team.</param>
/// <param name="Fcs">True for the FCS teams that exist to test the FBS-only rule.</param>
public sealed record CatalogTeam(
    int Id,
    string School,
    string? Mascot,
    string? Abbreviation,
    string? Conference,
    bool Fcs = false);
