namespace NcaafPickEm.Shared.Contracts.Scoring;

/// <summary>Body of <c>POST .../gameset/games/{gameId}/void</c> (commissioner, after lock only).</summary>
/// <param name="Reason">Free text recorded in the audit log, 1..200 characters.</param>
public sealed record VoidGameRequest(string Reason);
