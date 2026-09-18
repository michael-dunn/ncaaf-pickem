using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Admin;

/// <summary>
/// A provider game that could not be tied to a <c>Games</c> row and is waiting for a
/// commissioner to resolve it (<c>UnmatchedGames</c>).
/// </summary>
/// <param name="Id">The unmatched row, for <c>POST /api/admin/unmatched/{id}/resolve</c>.</param>
/// <param name="Source">Which provider sent it.</param>
/// <param name="RawHomeName">The home team exactly as the provider named it.</param>
/// <param name="RawAwayName">The away team exactly as the provider named it.</param>
/// <param name="GameDate">The game's calendar date.</param>
/// <param name="FirstSeenUtc">When it first arrived unmatched.</param>
public sealed record UnmatchedGameDto(
    Guid Id,
    ProviderSource Source,
    string RawHomeName,
    string RawAwayName,
    DateOnly GameDate,
    DateTimeOffset FirstSeenUtc);
