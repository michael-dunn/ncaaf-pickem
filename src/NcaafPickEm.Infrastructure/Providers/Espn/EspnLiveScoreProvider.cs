using System.Globalization;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers.Espn;

/// <summary>
/// Live scores from ESPN's public college-football scoreboard, the live-score source of record
/// (D-003). No API key, no published rate limit.
/// </summary>
/// <remarks>
/// One call per Eastern calendar date: <c>dates</c> accepts a single day (a range answers HTTP
/// 400) and buckets by Eastern time, so a Saturday call covers the whole slate including games
/// that finish after midnight Eastern. <c>groups=80</c> echoes the FBS intent but is not a
/// classification filter - FCS opponents are in the payload, which the matcher deals with.
/// </remarks>
public sealed class EspnLiveScoreProvider : ILiveScoreProvider
{
    /// <summary>Name of the <see cref="HttpClient"/> registered for ESPN.</summary>
    public const string HttpClientName = "Espn";

    /// <summary>Base address of the public scoreboard API.</summary>
    /// <summary>
    /// Sent on every ESPN request. ESPN's CDN answers 403 to requests with no User-Agent (the
    /// .NET HttpClient default), which showed up on the first deployment as a poller that never
    /// received a score. A plain product token is accepted.
    /// </summary>
    public const string UserAgent = "NcaafPickEm/1.0 (+https://github.com/michael-dunn/ncaaf-pickem)";

    public const string DefaultBaseAddress =
        "https://site.api.espn.com/apis/site/v2/sports/football/college-football/";

    private readonly HttpClient _httpClient;
    private readonly IProviderCallRecorder _recorder;
    private readonly ILogger<EspnLiveScoreProvider> _logger;

    /// <summary>Creates the provider.</summary>
    /// <param name="httpClient">The named ESPN client; see <see cref="HttpClientName"/>.</param>
    /// <param name="recorder">Records the call in <c>ProviderCalls</c>.</param>
    /// <param name="logger">Logger.</param>
    public EspnLiveScoreProvider(
        HttpClient httpClient,
        IProviderCallRecorder recorder,
        ILogger<EspnLiveScoreProvider> logger)
    {
        _httpClient = httpClient;
        _recorder = recorder;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LiveScoreUpdate>> GetScoresAsync(
        DateOnly easternDate,
        CancellationToken cancellationToken = default)
    {
        string path = string.Create(
            CultureInfo.InvariantCulture,
            $"scoreboard?groups=80&dates={easternDate:yyyyMMdd}&limit=300");

        string body = await _recorder.RecordAsync(
            ProviderSource.Espn,
            "GetScoreboard",
            async ct =>
            {
                using HttpResponseMessage response = await _httpClient.GetAsync(path, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<LiveScoreUpdate> updates = EspnScoreboardParser.Parse(body, _logger);
        _logger.LogInformation(
            "ESPN scoreboard for {EasternDate} returned {EventCount} usable events",
            easternDate,
            updates.Count);

        return updates;
    }
}
