using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers.Models;

/// <summary>
/// A team as any reference-data provider reports it (Feature 09, 12). CFBD's real
/// <c>alternateNames</c> feeds <see cref="AlternateNames"/> directly (see D-012's spike notes);
/// P2-02's ingest seeds <c>TeamAliases</c> from it unconditionally.
/// </summary>
public sealed record ProviderTeam(
    int CfbdId,
    string School,
    string? Mascot,
    string? Abbreviation,
    int? ConferenceCfbdId,
    TeamClassification Classification,
    string? LogoUrl,
    IReadOnlyList<string> AlternateNames);
