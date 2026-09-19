using CollegeFootballData;
using CollegeFootballData.Models;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers.Cfbd;

/// <summary>
/// <see cref="IReferenceDataProvider"/> backed by the real CollegeFootballData API, through the
/// official <c>CollegeFootballData</c> NuGet client (Kiota-generated). Registered when
/// <c>Providers:ReferenceData</c> is <c>Cfbd</c> (<c>Implementation/spikes/providers.md</c>).
/// </summary>
/// <remarks>
/// Every outbound call is wrapped in <see cref="IProviderCallRecorder"/> (the same singleton
/// P2-03's ESPN provider uses), which opens its own scope to write its <c>ProviderCalls</c> row,
/// so this provider itself is registered singleton too.
/// </remarks>
public sealed class CfbdReferenceDataProvider : IReferenceDataProvider
{
    private readonly ApiClient _client;
    private readonly IProviderCallRecorder _recorder;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the provider.</summary>
    /// <param name="client">The Kiota-generated CFBD client.</param>
    /// <param name="recorder">Writes one <c>ProviderCalls</c> row per outbound call.</param>
    /// <param name="timeProvider">The only clock this codebase may read (05-Conventions.md).</param>
    public CfbdReferenceDataProvider(ApiClient client, IProviderCallRecorder recorder, TimeProvider timeProvider)
    {
        _client = client;
        _recorder = recorder;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Every conference CFBD knows for the season, all divisions, not just FBS.
    /// </summary>
    /// <remarks>
    /// The <c>classification</c> filter is deliberately not sent. An FBS team's schedule contains
    /// FBS-vs-FCS games, and those games' FCS side has to resolve to a real <c>Teams</c> row with
    /// a real conference for the schedule ingest to store the game at all — see the decision in
    /// DECISIONS.md.
    /// </remarks>
    /// <param name="season">Season year.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<ProviderConference>> GetConferencesAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        List<Conference>? conferences = await _recorder.RecordAsync(
            ProviderSource.Cfbd,
            "GetConferences",
            token => _client.Conferences.GetAsync(
                cfg => cfg.QueryParameters.Year = season,
                token),
            cancellationToken).ConfigureAwait(false);

        return MapMany(conferences, CfbdMapping.MapConference);
    }

    /// <summary>
    /// Every team CFBD knows for the season, all divisions (<c>GET /teams?year=</c>), not the
    /// FBS-only <c>GET /teams/fbs</c>.
    /// </summary>
    /// <remarks>
    /// <c>GET /games?classification=fbs</c> returns games *involving* an FBS team, so an FBS-vs-FCS
    /// game's away side is an FCS school. <c>ReferenceDataIngestService.IngestScheduleAsync</c>
    /// skips any game whose home or away id is missing from <c>Teams</c>, so fetching only the FBS
    /// list would drop every one of those games. Eligibility for a game set is still decided by the
    /// generator (both teams FBS), and P2-03's matcher needs the FCS schools to be known so it can
    /// ignore them rather than write <c>UnmatchedGames</c> noise. See DECISIONS.md.
    /// </remarks>
    /// <param name="season">Season year.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<ProviderTeam>> GetTeamsAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        // Every team's alternateNames feeds TeamAliases directly (Implementation/spikes/
        // providers.md); fetching conferences first lets teams resolve ConferenceCfbdId by name
        // without a second round trip per team.
        IReadOnlyList<ProviderConference> conferences =
            await GetConferencesAsync(season, cancellationToken).ConfigureAwait(false);
        Dictionary<string, int> conferenceIdsByName = conferences
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().CfbdId, StringComparer.OrdinalIgnoreCase);

        List<Team>? teams = await _recorder.RecordAsync(
            ProviderSource.Cfbd,
            "GetTeams",
            token => _client.Teams.GetAsync(cfg => cfg.QueryParameters.Year = season, token),
            cancellationToken).ConfigureAwait(false);

        return MapMany(teams, team => CfbdMapping.MapTeam(team, conferenceIdsByName));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderGame>> GetGamesAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default)
    {
        List<Game>? games = await _recorder.RecordAsync(
            ProviderSource.Cfbd,
            "GetGames",
            token => _client.Games.GetAsync(
                cfg =>
                {
                    cfg.QueryParameters.Year = season;
                    cfg.QueryParameters.Week = week;
                    cfg.QueryParameters.SeasonTypeAsSeasonType = SeasonType.Regular;
                    cfg.QueryParameters.ClassificationAsDivisionClassification = DivisionClassification.Fbs;
                },
                token),
            cancellationToken).ConfigureAwait(false);

        return MapMany(games, CfbdMapping.MapGame);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderRanking>> GetRankingsAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default)
    {
        List<PollWeek>? pollWeeks = await _recorder.RecordAsync(
            ProviderSource.Cfbd,
            "GetRankings",
            token => _client.Rankings.GetAsync(
                cfg =>
                {
                    cfg.QueryParameters.Year = season;
                    cfg.QueryParameters.Week = week;
                    cfg.QueryParameters.SeasonTypeAsSeasonType = SeasonType.Regular;
                },
                token),
            cancellationToken).ConfigureAwait(false);

        if (pollWeeks is null)
        {
            return [];
        }

        List<ProviderRanking> result = [];
        foreach (PollWeek pollWeek in pollWeeks)
        {
            int pollSeason = pollWeek.Season ?? season;
            int pollWeekNumber = pollWeek.Week ?? week;

            // CFBD's /rankings response carries every poll (Coaches, FCS, both AFCA polls, ...);
            // Feature 09 wants AP only, so filter on the poll's own name rather than array
            // position (Implementation/spikes/providers.md).
            Poll? apPoll = pollWeek.Polls?.FirstOrDefault(
                p => string.Equals(p.PollProp, "AP Top 25", StringComparison.OrdinalIgnoreCase));

            if (apPoll?.Ranks is null)
            {
                continue;
            }

            foreach (PollRank rank in apPoll.Ranks)
            {
                if (CfbdMapping.MapApRank(pollSeason, pollWeekNumber, rank) is ProviderRanking mapped)
                {
                    result.Add(mapped);
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderLine>> GetLinesAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default)
    {
        List<BettingGame>? bettingGames = await _recorder.RecordAsync(
            ProviderSource.Cfbd,
            "GetLines",
            token => _client.Lines.GetAsync(
                cfg =>
                {
                    cfg.QueryParameters.Year = season;
                    cfg.QueryParameters.Week = week;
                    cfg.QueryParameters.SeasonTypeAsSeasonType = SeasonType.Regular;
                },
                token),
            cancellationToken).ConfigureAwait(false);

        if (bettingGames is null)
        {
            return [];
        }

        DateTime fetchedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        List<ProviderLine> result = [];
        foreach (BettingGame bettingGame in bettingGames)
        {
            if (bettingGame.Id is not int cfbdGameId || bettingGame.Lines is null)
            {
                continue;
            }

            foreach (GameLine line in bettingGame.Lines)
            {
                if (CfbdMapping.MapLine(cfbdGameId, line, fetchedUtc) is ProviderLine mapped)
                {
                    result.Add(mapped);
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderCalendarWeek>> GetCalendarAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        List<CalendarWeek>? weeks = await _recorder.RecordAsync(
            ProviderSource.Cfbd,
            "GetCalendar",
            token => _client.Calendar.GetAsync(cfg => cfg.QueryParameters.Year = season, token),
            cancellationToken).ConfigureAwait(false);

        return MapMany(weeks, CfbdMapping.MapCalendarWeek);
    }

    private static IReadOnlyList<TOut> MapMany<TIn, TOut>(List<TIn>? source, Func<TIn, TOut?> map)
        where TOut : class
    {
        if (source is null)
        {
            return [];
        }

        List<TOut> result = [];
        foreach (TIn item in source)
        {
            if (map(item) is TOut mapped)
            {
                result.Add(mapped);
            }
        }

        return result;
    }
}
