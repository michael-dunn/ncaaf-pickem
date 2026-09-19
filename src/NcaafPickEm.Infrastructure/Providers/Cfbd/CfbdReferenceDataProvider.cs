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

    /// <summary>Creates the provider.</summary>
    public CfbdReferenceDataProvider(ApiClient client, IProviderCallRecorder recorder)
    {
        _client = client;
        _recorder = recorder;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderConference>> GetConferencesAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        List<Conference>? conferences = await _recorder.RecordAsync(
            ProviderSource.Cfbd,
            "GetConferences",
            token => _client.Conferences.GetAsync(
                cfg =>
                {
                    cfg.QueryParameters.Year = season;
                    cfg.QueryParameters.ClassificationAsConferenceClassification = ConferenceClassification.Fbs;
                },
                token),
            cancellationToken).ConfigureAwait(false);

        return MapMany(conferences, CfbdMapping.MapConference);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderTeam>> GetTeamsAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        // Every FBS team's alternateNames feeds TeamAliases directly (Implementation/spikes/
        // providers.md); fetching conferences first lets teams resolve ConferenceCfbdId by name
        // without a second round trip per team.
        IReadOnlyList<ProviderConference> conferences =
            await GetConferencesAsync(season, cancellationToken).ConfigureAwait(false);
        Dictionary<string, int> conferenceIdsByName = conferences
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().CfbdId, StringComparer.OrdinalIgnoreCase);

        List<Team>? teams = await _recorder.RecordAsync(
            ProviderSource.Cfbd,
            "GetFbsTeams",
            token => _client.Teams.Fbs.GetAsync(cfg => cfg.QueryParameters.Year = season, token),
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

        DateTime fetchedUtc = DateTime.UtcNow;
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
