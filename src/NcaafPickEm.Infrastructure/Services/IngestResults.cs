namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Result of <see cref="ReferenceDataIngestService.IngestTeamsAsync"/>: counts of the
/// <c>Conferences</c>/<c>Teams</c>/<c>TeamAliases</c> rows the provider's payload named, whether
/// they were new or already existed.
/// </summary>
public sealed record TeamsIngestResult(bool Success, string? Error, int Conferences, int Teams, int Aliases);

/// <summary>
/// Result of <see cref="ReferenceDataIngestService.IngestCalendarAsync"/>: count of
/// <c>SeasonWeeks</c> rows the provider's calendar named.
/// </summary>
public sealed record CalendarIngestResult(bool Success, string? Error, int Weeks);

/// <summary>
/// Result of <see cref="ReferenceDataIngestService.IngestScheduleAsync"/>. <see cref="Upserted"/>
/// counts every game in the payload; <see cref="Postponed"/> and <see cref="Restored"/> count
/// games whose status moved because of what was — or was not — in this fetch.
/// </summary>
public sealed record ScheduleIngestResult(bool Success, string? Error, int Upserted, int Postponed, int Restored);

/// <summary>
/// Result of <see cref="ReferenceDataIngestService.IngestRankingsAsync"/>: count of AP poll ranks
/// written for the week.
/// </summary>
public sealed record RankingsIngestResult(bool Success, string? Error, int Rankings);

/// <summary>
/// Result of <see cref="ReferenceDataIngestService.IngestLinesAsync"/>: count of new
/// <c>GameLines</c> history rows appended (unchanged spreads add nothing).
/// </summary>
public sealed record LinesIngestResult(bool Success, string? Error, int Lines);
