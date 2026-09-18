namespace NcaafPickEm.Infrastructure.Providers.Models;

/// <summary>
/// A scheduled game as CFBD reports it (Feature 09). CFBD's <c>Game</c> carries no status field —
/// only <see cref="Completed"/> and <see cref="StartTimeTbd"/> (D-012) — so postponement must be
/// detected by the ingest as the game disappearing from, or moving within, the week's payload.
/// </summary>
public sealed record ProviderGame(
    long CfbdGameId,
    int Season,
    int Week,
    int HomeCfbdTeamId,
    int AwayCfbdTeamId,
    DateTime KickoffUtc,
    bool StartTimeTbd,
    bool IsConferenceGame,
    bool NeutralSite,
    bool Completed,
    int? HomePoints,
    int? AwayPoints,
    string? Venue);
