using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Infrastructure.Providers.Fixture;

/// <summary>
/// The season calendar without a provider: the 2026 season, Week 0 through championship week,
/// built from plausible Saturdays. Lets every offline development path (Providers:* = Fixture)
/// answer "what week is it?" before P2-02 ingests the real CFBD calendar.
/// </summary>
/// <remarks>
/// The weeks are code, not JSON, so this file never collides with P2-05's fixture data set.
/// </remarks>
public sealed class FixtureSeasonWeekSource : ISeasonWeekSource
{
    /// <summary>The only season this source knows.</summary>
    public const int FixtureSeasonYear = 2026;

    /// <summary>Saturday of Week 1 of the 2026 season.</summary>
    private static readonly DateOnly WeekOneSaturday = new(2026, 9, 5);

    /// <summary>Last week of the season: conference championship week, not regular season.</summary>
    private const int ChampionshipWeek = 15;

    private static readonly IReadOnlyList<SeasonWeek> Weeks2026 = BuildWeeks();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<SeasonWeek>> GetWeeksAsync(
        int seasonYear,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<SeasonWeek> weeks = seasonYear == FixtureSeasonYear ? Weeks2026 : [];
        return ValueTask.FromResult(weeks);
    }

    private static SeasonWeek[] BuildWeeks()
    {
        SeasonWeek[] weeks = new SeasonWeek[ChampionshipWeek + 1];

        for (int week = 0; week <= ChampionshipWeek; week++)
        {
            // Week 0 is the Saturday before Week 1; every later week is a Saturday after it.
            DateOnly saturday = WeekOneSaturday.AddDays(7 * (week - 1));
            (DateTimeOffset startUtc, DateTimeOffset endUtc) = SeasonCalendar.WeekWindow(saturday);

            weeks[week] = new SeasonWeek(
                FixtureSeasonYear,
                week,
                startUtc,
                endUtc,
                IsRegularSeason: week < ChampionshipWeek);
        }

        return weeks;
    }
}
