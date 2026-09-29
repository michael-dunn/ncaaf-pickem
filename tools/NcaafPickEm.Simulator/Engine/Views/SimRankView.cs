namespace NcaafPickEm.Simulator.Engine.Views;

/// <summary>One line of the week's AP poll.</summary>
/// <param name="Rank">1..25.</param>
/// <param name="TeamId">The team.</param>
/// <param name="School">School name.</param>
public sealed record SimRankView(int Rank, Guid TeamId, string School);
