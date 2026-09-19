using CollegeFootballData.Models;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;
using CfbdConference = CollegeFootballData.Models.Conference;
using CfbdGame = CollegeFootballData.Models.Game;
using CfbdGameLine = CollegeFootballData.Models.GameLine;
using CfbdTeam = CollegeFootballData.Models.Team;
using DomainRanking = NcaafPickEm.Domain.Seasons.Ranking;

namespace NcaafPickEm.Infrastructure.Providers.Cfbd;

/// <summary>
/// Pure mapping from the Kiota-generated CFBD models to this codebase's provider-neutral records
/// (<c>Implementation/spikes/providers.md</c>). Kept separate from
/// <see cref="CfbdReferenceDataProvider"/>, and public, so <c>CfbdMappingTests</c> can drive it
/// directly against the real captures in <c>tests/NcaafPickEm.Fixtures/Real/</c> without a live
/// call or a database.
/// </summary>
/// <remarks>
/// Every Kiota model property is nullable regardless of what the OpenAPI spec calls required
/// (the spike's finding, confirmed again here), so every method below null-checks rather than
/// null-forgives, and skips a row it cannot map rather than throwing.
/// </remarks>
public static class CfbdMapping
{
    /// <summary>
    /// Maps one <see cref="CfbdConference"/>. Returns null when the row has no <c>id</c> or
    /// <c>name</c> — nothing to key an upsert on.
    /// </summary>
    /// <remarks>
    /// <c>Name</c> comes from CFBD's <c>name</c> field, not <c>shortName</c>: CFBD's own
    /// <c>Team.conference</c> string (e.g. "Sun Belt") matches <c>name</c>, not the long form
    /// <c>shortName</c> ("Sun Belt Conference") — confirmed against the real
    /// <c>cfbd-conferences-2025.json</c> and <c>cfbd-teams-fbs.json</c> captures. Joining
    /// <see cref="ProviderGame"/>/<see cref="ProviderTeam"/> rows to a conference by name only
    /// works if <c>Conferences.Name</c> uses the same short form.
    /// </remarks>
    public static ProviderConference? MapConference(CfbdConference conference)
    {
        ArgumentNullException.ThrowIfNull(conference);

        if (conference.Id is not int id || string.IsNullOrWhiteSpace(conference.Name))
        {
            return null;
        }

        return new ProviderConference(
            id,
            conference.Name,
            conference.Abbreviation ?? string.Empty,
            MapClassification(conference.Classification?.ToString()));
    }

    /// <summary>
    /// Maps one <see cref="CfbdTeam"/>. Returns null when the row has no <c>id</c> or
    /// <c>school</c> — nothing to key an upsert on.
    /// </summary>
    /// <remarks>
    /// Deliberately does not read <see cref="CfbdTeam.Location"/> for anything: that property is
    /// a <see cref="Venue"/> (the stadium), not the school name — the exact name collision with
    /// ESPN's own <c>team.location</c> (the school name) the spike calls out. <c>School</c> is
    /// always the match key.
    /// </remarks>
    public static ProviderTeam? MapTeam(CfbdTeam team, IReadOnlyDictionary<string, int> conferenceIdsByName)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(conferenceIdsByName);

        if (team.Id is not int id || string.IsNullOrWhiteSpace(team.School))
        {
            return null;
        }

        int? conferenceCfbdId = team.Conference is { Length: > 0 } conferenceName
            && conferenceIdsByName.TryGetValue(conferenceName, out int mappedId)
                ? mappedId
                : null;

        return new ProviderTeam(
            id,
            team.School,
            team.Mascot,
            team.Abbreviation,
            conferenceCfbdId,
            MapClassification(team.Classification),
            team.Logos?.Count > 0 ? team.Logos[0] : null,
            team.AlternateNames ?? []);
    }

    /// <summary>
    /// Maps one <see cref="CfbdGame"/>. Returns null when the row has no <c>id</c>, <c>homeId</c>,
    /// <c>awayId</c>, or <c>startDate</c> — the ingest cannot place it without those.
    /// </summary>
    public static ProviderGame? MapGame(CfbdGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (game.Id is not int id
            || game.HomeId is not int homeId
            || game.AwayId is not int awayId
            || game.StartDate is not DateTimeOffset startDate)
        {
            return null;
        }

        return new ProviderGame(
            id,
            game.Season ?? 0,
            game.Week ?? 0,
            homeId,
            awayId,
            startDate.UtcDateTime,
            game.StartTimeTBD ?? false,
            game.ConferenceGame ?? false,
            game.NeutralSite ?? false,
            game.Completed ?? false,
            game.HomePoints,
            game.AwayPoints,
            game.Venue);
    }

    /// <summary>
    /// Maps one AP poll rank to a <see cref="ProviderRanking"/>. Returns null when the row has
    /// no <c>rank</c> or <c>teamId</c>.
    /// </summary>
    public static ProviderRanking? MapApRank(int season, int week, PollRank rank)
    {
        ArgumentNullException.ThrowIfNull(rank);

        if (rank.Rank is not int rankValue || rank.TeamId is not int teamId)
        {
            return null;
        }

        return new ProviderRanking(season, week, DomainRanking.ApPoll, rankValue, teamId);
    }

    /// <summary>
    /// Maps one betting line to a <see cref="ProviderLine"/>. Returns null when the row has no
    /// <c>provider</c> name.
    /// </summary>
    public static ProviderLine? MapLine(long cfbdGameId, CfbdGameLine line, DateTime fetchedUtc)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (string.IsNullOrWhiteSpace(line.Provider))
        {
            return null;
        }

        return new ProviderLine(cfbdGameId, line.Provider, MapSpread(line.Spread), fetchedUtc);
    }

    /// <summary>
    /// Maps one <see cref="CalendarWeek"/>. Returns null when the row has no <c>week</c> or
    /// window.
    /// </summary>
    public static ProviderCalendarWeek? MapCalendarWeek(CalendarWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);

        if (week.Week is not int weekNumber
            || week.StartDate is not DateTimeOffset startDate
            || week.EndDate is not DateTimeOffset endDate)
        {
            return null;
        }

        return new ProviderCalendarWeek(
            week.Season ?? 0,
            weekNumber,
            week.SeasonType?.ToString() ?? string.Empty,
            startDate.UtcDateTime,
            endDate.UtcDateTime);
    }

    /// <summary>
    /// CFBD's <c>spread</c> is already home-relative (negative = home favored) on both provider
    /// endpoints, per the spike's live cross-check — no sign conversion.
    /// </summary>
    private static decimal? MapSpread(double? spread) => spread is double value ? (decimal)value : null;

    /// <summary>
    /// Maps CFBD's <c>classification</c> string (<c>"fbs"</c>, <c>"fcs"</c>, <c>"ii"</c>,
    /// <c>"ii/iii"</c>, <c>"iii"</c>, or the enum's own <c>.ToString()</c> spelling) to
    /// <see cref="TeamClassification"/>. Anything unrecognized, including null, maps to
    /// <see cref="TeamClassification.Other"/> rather than throwing — CFBD adds classifications
    /// (D2, D3, NAIA) this product never schedules games for.
    /// </summary>
    public static TeamClassification MapClassification(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "fbs" => TeamClassification.Fbs,
            "fcs" => TeamClassification.Fcs,
            _ => TeamClassification.Other,
        };
}
