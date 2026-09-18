namespace NcaafPickEm.Infrastructure.Providers.Models;

/// <summary>
/// One slot in one poll for one week (Feature 09). Only the AP poll is ingested (<c>Poll</c> =
/// <c>"AP"</c>). CFBD's <c>PollRank</c> carries a <c>teamId</c>, which is sturdier to join on than
/// the school name, so <see cref="CfbdTeamId"/> is always populated for CFBD-sourced rankings.
/// </summary>
public sealed record ProviderRanking(
    int Season,
    int Week,
    string Poll,
    int Rank,
    int CfbdTeamId);
