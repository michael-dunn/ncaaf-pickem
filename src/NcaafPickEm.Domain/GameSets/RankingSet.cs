namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// One AP Top 25 poll as ingested: the week it was released for, when it was fetched, and the
/// teams in it. Callers pass every poll they hold and the generator picks the one to use, rather
/// than the caller pre-selecting: the poll for the week being generated with the latest
/// <see cref="FetchedUtc"/>, otherwise the most recent prior week's poll, which raises
/// <see cref="GenerationResult.UsedFallbackRankings"/>. Polls for later weeks are ignored.
/// </summary>
/// <param name="Week">The provider week the poll was released for.</param>
/// <param name="FetchedUtc">When the poll was fetched; breaks ties within a week.</param>
/// <param name="RankedTeamIds">
/// Team ids in the poll. Ranks themselves do not affect selection, only membership.
/// </param>
public sealed record RankingSet(
    int Week,
    DateTime FetchedUtc,
    IReadOnlySet<Guid> RankedTeamIds);
