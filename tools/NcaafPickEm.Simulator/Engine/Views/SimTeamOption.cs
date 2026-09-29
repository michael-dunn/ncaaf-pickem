namespace NcaafPickEm.Simulator.Engine.Views;

/// <summary>A team for the add-game and poll pickers.</summary>
/// <param name="TeamId">The team.</param>
/// <param name="School">School name.</param>
/// <param name="Conference">Conference abbreviation, or "FCS".</param>
public sealed record SimTeamOption(Guid TeamId, string School, string Conference);
