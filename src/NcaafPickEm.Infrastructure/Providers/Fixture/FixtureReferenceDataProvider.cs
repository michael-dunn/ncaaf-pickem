using NcaafPickEm.Fixtures;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers.Fixture;

/// <summary>
/// Reference data for the Week 7, 2026 sample week, read from the embedded fixtures in
/// <c>tests/NcaafPickEm.Fixtures/Data/Week7_2026</c>. Registered as
/// <see cref="IReferenceDataProvider"/> when <c>Providers:ReferenceData</c> is <c>Fixture</c>
/// (the Development default).
/// </summary>
public sealed class FixtureReferenceDataProvider : IReferenceDataProvider
{
    /// <summary>The only season/week this provider knows.</summary>
    public const int FixtureSeason = 2026;

    /// <summary>The only season/week this provider knows.</summary>
    public const int FixtureWeek = 7;

    private const string FixtureRoot = "Week7_2026";

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderConference>> GetConferencesAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        if (season != FixtureSeason)
        {
            return Task.FromResult<IReadOnlyList<ProviderConference>>([]);
        }

        FixtureFile<RawConference> file = FixtureLoader.Read<FixtureFile<RawConference>>($"{FixtureRoot}/conferences.json");
        IReadOnlyList<ProviderConference> result =
        [
            .. file.Data.Select(c => new ProviderConference(
                c.CfbdId,
                c.Name,
                c.Abbreviation,
                ParseClassification(c.Classification))),
        ];

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderTeam>> GetTeamsAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        if (season != FixtureSeason)
        {
            return Task.FromResult<IReadOnlyList<ProviderTeam>>([]);
        }

        FixtureFile<RawTeam> file = FixtureLoader.Read<FixtureFile<RawTeam>>($"{FixtureRoot}/teams.json");
        IReadOnlyList<ProviderTeam> result =
        [
            .. file.Data.Select(t => new ProviderTeam(
                t.CfbdId,
                t.School,
                t.Mascot,
                t.Abbreviation,
                t.ConferenceCfbdId,
                ParseClassification(t.Classification),
                t.LogoUrl,
                t.AlternateNames ?? [])),
        ];

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderGame>> GetGamesAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default)
    {
        if (season != FixtureSeason || week != FixtureWeek)
        {
            return Task.FromResult<IReadOnlyList<ProviderGame>>([]);
        }

        FixtureFile<RawGame> file = FixtureLoader.Read<FixtureFile<RawGame>>($"{FixtureRoot}/schedule.json");
        IReadOnlyList<ProviderGame> result =
        [
            .. file.Data.Select(g => new ProviderGame(
                g.CfbdGameId,
                g.Season,
                g.Week,
                g.HomeCfbdTeamId,
                g.AwayCfbdTeamId,
                g.KickoffUtc,
                g.StartTimeTbd,
                g.IsConferenceGame,
                g.NeutralSite,
                g.Completed,
                g.HomePoints,
                g.AwayPoints,
                g.Venue)),
        ];

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderRanking>> GetRankingsAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default)
    {
        if (season != FixtureSeason || week != FixtureWeek)
        {
            return Task.FromResult<IReadOnlyList<ProviderRanking>>([]);
        }

        FixtureFile<RawRanking> file = FixtureLoader.Read<FixtureFile<RawRanking>>($"{FixtureRoot}/rankings.json");
        IReadOnlyList<ProviderRanking> result =
        [
            .. file.Data.Select(r => new ProviderRanking(r.Season, r.Week, r.Poll, r.Rank, r.CfbdTeamId)),
        ];

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderLine>> GetLinesAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default)
    {
        if (season != FixtureSeason || week != FixtureWeek)
        {
            return Task.FromResult<IReadOnlyList<ProviderLine>>([]);
        }

        FixtureFile<RawLine> file = FixtureLoader.Read<FixtureFile<RawLine>>($"{FixtureRoot}/lines.json");
        IReadOnlyList<ProviderLine> result =
        [
            .. file.Data.Select(l => new ProviderLine(l.CfbdGameId, l.Provider, l.Spread, l.FetchedUtc)),
        ];

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderCalendarWeek>> GetCalendarAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        // The season calendar is supplied by ISeasonWeekSource (P0-05), not this provider; the
        // fixture data set has no calendar.json. Empty result is the honest answer for any caller
        // that still asks this provider directly.
        return Task.FromResult<IReadOnlyList<ProviderCalendarWeek>>([]);
    }

    private static TeamClassification ParseClassification(string raw) =>
        Enum.TryParse<TeamClassification>(raw, ignoreCase: true, out TeamClassification parsed)
            ? parsed
            : TeamClassification.Other;

    private sealed record FixtureFile<T>(IReadOnlyList<T> Data);

    private sealed record RawConference(int CfbdId, string Name, string Abbreviation, string Classification);

    private sealed record RawTeam(
        int CfbdId,
        string School,
        string? Mascot,
        string? Abbreviation,
        int? ConferenceCfbdId,
        string Classification,
        string? LogoUrl,
        IReadOnlyList<string>? AlternateNames);

    private sealed record RawGame(
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

    private sealed record RawRanking(int Season, int Week, string Poll, int Rank, int CfbdTeamId);

    private sealed record RawLine(long CfbdGameId, string Provider, decimal? Spread, DateTime FetchedUtc);
}
