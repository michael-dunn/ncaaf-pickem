namespace NcaafPickEm.Shared.Contracts.Scoring;

/// <summary>Body of <c>POST .../gameset/games/{gameId}/override-result</c> (commissioner, after lock only).</summary>
/// <param name="WinnerTeamId">Must be the game's home or away team.</param>
/// <param name="Reason">Free text recorded in the audit log, 1..200 characters.</param>
public sealed record OverrideResultRequest(
    Guid WinnerTeamId,
    string Reason);
