namespace NcaafPickEm.Infrastructure.Providers.Models;

/// <summary>
/// A betting line for one game from one sportsbook, at the moment it was fetched (Feature 09, 12).
/// <see cref="Spread"/> is home-relative: negative means the home team is favored (D-013;
/// confirmed against real CFBD and ESPN captures — no sign conversion needed from either
/// provider).
/// </summary>
public sealed record ProviderLine(
    long CfbdGameId,
    string Provider,
    decimal? Spread,
    DateTime FetchedUtc);
