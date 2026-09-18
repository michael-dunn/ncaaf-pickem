namespace NcaafPickEm.Shared.Contracts.GameSets;

/// <summary>Body of <c>POST /api/leagues/{leagueId}/weeks/{week}/gameset/preview</c>.</summary>
/// <param name="Games">Games the candidate rules would select, ordered by kickoff.</param>
/// <param name="Count">Number of games.</param>
/// <param name="ExceedsMax">True when <paramref name="Count"/> is over the 50-game cap; generate would be refused.</param>
/// <param name="UsedFallbackRankings">True when no AP poll exists for the week and the prior week's poll was used.</param>
public sealed record GameSetPreview(
    GameSetGameDto[] Games,
    int Count,
    bool ExceedsMax,
    bool UsedFallbackRankings);
