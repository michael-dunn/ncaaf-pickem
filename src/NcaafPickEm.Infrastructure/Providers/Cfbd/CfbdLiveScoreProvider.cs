using System.Globalization;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers.Cfbd;

/// <summary>
/// The fallback live-score source: CollegeFootballData's games endpoint for the week that
/// contains the requested Eastern date (D-003, 04-Domain-Algorithms.md section 10).
/// </summary>
/// <remarks>
/// CFBD's <c>Game</c> object has no status field at all - only <c>completed</c> - so this
/// provider can produce nothing but Scheduled and Final, with no period, no clock, no live odds
/// and no Postponed or Cancelled (D-012). That is what "scores may be stale" on the dashboard
/// really means when the fallback is engaged. Its updates carry
/// <see cref="ProviderSource.Cfbd"/> and put the <c>CfbdGameId</c> in
/// <see cref="LiveScoreUpdate.SourceEventId"/>, because CFBD gives no team names here and the
/// matcher identifies these games by their natural key instead.
/// </remarks>
public sealed class CfbdLiveScoreProvider : ILiveScoreProvider
{
    private readonly IReferenceDataProvider _referenceData;
    private readonly ISeasonWeekSource _seasonWeeks;
    private readonly ILogger<CfbdLiveScoreProvider> _logger;

    /// <summary>Creates the provider.</summary>
    /// <param name="referenceData">The configured reference-data provider (CFBD in production).</param>
    /// <param name="seasonWeeks">Resolves which week contains the requested date.</param>
    /// <param name="logger">Logger.</param>
    public CfbdLiveScoreProvider(
        IReferenceDataProvider referenceData,
        ISeasonWeekSource seasonWeeks,
        ILogger<CfbdLiveScoreProvider> logger)
    {
        _referenceData = referenceData;
        _seasonWeeks = seasonWeeks;
        _logger = logger;
    }

    /// <summary>
    /// The season a date belongs to. A college season is named for the calendar year it starts
    /// in, so January's bowls belong to the previous season.
    /// </summary>
    /// <param name="easternDate">An Eastern calendar date.</param>
    public static int SeasonYearFor(DateOnly easternDate) =>
        easternDate.Month == 1 ? easternDate.Year - 1 : easternDate.Year;

    /// <inheritdoc />
    public async Task<IReadOnlyList<LiveScoreUpdate>> GetScoresAsync(
        DateOnly easternDate,
        CancellationToken cancellationToken = default)
    {
        int season = SeasonYearFor(easternDate);

        // Midday Eastern is inside the day whatever the DST offset is, so it is a safe probe for
        // "which week is this date in".
        DateTimeOffset middayUtc = SeasonCalendar.ToUtc(easternDate.ToDateTime(new TimeOnly(12, 0)));

        IReadOnlyList<SeasonWeek> weeks = await _seasonWeeks
            .GetWeeksAsync(season, cancellationToken)
            .ConfigureAwait(false);

        SeasonWeek? week = weeks.FirstOrDefault(w => w.Contains(middayUtc));
        if (week is null)
        {
            _logger.LogWarning(
                "No {Season} week contains {EasternDate}; the CFBD fallback has nothing to return",
                season,
                easternDate);
            return [];
        }

        IReadOnlyList<ProviderGame> games = await _referenceData
            .GetGamesAsync(season, week.Week, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<LiveScoreUpdate> updates =
        [
            .. games.Where(g => KickoffEasternDate(g.KickoffUtc) == easternDate).Select(ToUpdate),
        ];

        _logger.LogInformation(
            "CFBD fallback for {EasternDate} (season {Season} week {Week}) returned {GameCount} games",
            easternDate,
            season,
            week.Week,
            updates.Count);

        return updates;
    }

    private static DateOnly KickoffEasternDate(DateTime kickoffUtc)
    {
        DateTimeOffset utc = new(DateTime.SpecifyKind(kickoffUtc, DateTimeKind.Utc));
        return DateOnly.FromDateTime(SeasonCalendar.ToEastern(utc).DateTime);
    }

    private static LiveScoreUpdate ToUpdate(ProviderGame game)
    {
        GameStatus status = game.Completed ? GameStatus.Final : GameStatus.Scheduled;

        return new LiveScoreUpdate(
            game.CfbdGameId.ToString(CultureInfo.InvariantCulture),
            game.KickoffUtc,
            string.Empty,
            string.Empty,
            game.HomeCfbdTeamId.ToString(CultureInfo.InvariantCulture),
            status == GameStatus.Final ? game.HomePoints : null,
            string.Empty,
            string.Empty,
            game.AwayCfbdTeamId.ToString(CultureInfo.InvariantCulture),
            status == GameStatus.Final ? game.AwayPoints : null,
            status,
            game.Completed ? "completed" : "scheduled",
            game.Completed,
            null,
            null,
            null,
            ProviderSource.Cfbd);
    }
}
