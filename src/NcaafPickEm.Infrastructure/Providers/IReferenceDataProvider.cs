using NcaafPickEm.Infrastructure.Providers.Models;

namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>
/// Reference data (teams, conferences, schedule, rankings, lines, calendar) from one provider
/// (Feature 09, 12). <c>Providers:ReferenceData</c> selects the implementation registered against
/// this interface: <see cref="Fixture.FixtureReferenceDataProvider"/> today; P2-02 adds
/// <c>CfbdReferenceDataProvider</c>.
/// </summary>
public interface IReferenceDataProvider
{
    /// <summary>Every FBS conference for a season.</summary>
    Task<IReadOnlyList<ProviderConference>> GetConferencesAsync(int season, CancellationToken cancellationToken = default);

    /// <summary>Every team the provider knows for a season, FBS and FCS alike.</summary>
    Task<IReadOnlyList<ProviderTeam>> GetTeamsAsync(int season, CancellationToken cancellationToken = default);

    /// <summary>Every scheduled game for a season and provider week.</summary>
    Task<IReadOnlyList<ProviderGame>> GetGamesAsync(int season, int week, CancellationToken cancellationToken = default);

    /// <summary>AP poll rankings for a season and provider week.</summary>
    Task<IReadOnlyList<ProviderRanking>> GetRankingsAsync(int season, int week, CancellationToken cancellationToken = default);

    /// <summary>Betting lines for every game in a season and provider week.</summary>
    Task<IReadOnlyList<ProviderLine>> GetLinesAsync(int season, int week, CancellationToken cancellationToken = default);

    /// <summary>The season's week calendar, as the provider defines its boundaries.</summary>
    Task<IReadOnlyList<ProviderCalendarWeek>> GetCalendarAsync(int season, CancellationToken cancellationToken = default);
}
