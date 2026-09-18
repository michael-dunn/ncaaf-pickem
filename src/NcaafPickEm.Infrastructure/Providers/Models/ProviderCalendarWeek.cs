namespace NcaafPickEm.Infrastructure.Providers.Models;

/// <summary>
/// One week of CFBD's season calendar (Feature 13). <see cref="StartUtc"/>/<see cref="EndUtc"/>
/// are CFBD's own Eastern-anchored boundaries (Monday 03:00 ET through the following Monday
/// 02:59 ET, D-013) — the ingest decides whether to store them verbatim or normalize them to the
/// Sunday-Saturday <c>SeasonWeeks</c> window; this record just carries what the provider said.
/// </summary>
public sealed record ProviderCalendarWeek(
    int Season,
    int Week,
    string SeasonType,
    DateTime StartUtc,
    DateTime EndUtc);
