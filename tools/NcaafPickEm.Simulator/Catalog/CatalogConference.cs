namespace NcaafPickEm.Simulator.Catalog;

/// <summary>One FBS conference in the simulator's catalog.</summary>
/// <param name="CfbdId">CFBD conference id.</param>
/// <param name="Name">Full name, e.g. "Southeastern Conference".</param>
/// <param name="Abbreviation">Short name the catalog's teams refer to, e.g. <c>SEC</c>.</param>
public sealed record CatalogConference(int CfbdId, string Name, string Abbreviation);
