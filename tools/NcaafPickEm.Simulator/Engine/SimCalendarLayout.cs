using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Lays out a simulated season's <c>SeasonWeeks</c> around the week under test (P11-01, D-185):
/// that week starts at an instant the tester chose (normally "now") and runs seven days; every
/// earlier week sits back-to-back behind it, one day each, and every later week follows it, seven
/// days each. The windows are contiguous and ordered, which is all
/// <see cref="SeasonCalendar.CurrentWeekAt"/> needs, so the app's own calendar logic decides
/// "current week" exactly as it does for a real season.
/// </summary>
/// <remarks>
/// Pure: no clock, no database. The shape mirrors what the CFBD ingest stores (D-167): weeks 1
/// through <see cref="ChampionshipWeek"/>, only the last flagged not regular season, so a league
/// defaults to weeks 1..14.
/// </remarks>
public static class SimCalendarLayout
{
    /// <summary>First week of a simulated season.</summary>
    public const int FirstWeek = 1;

    /// <summary>Conference championship week: in the calendar, never playable.</summary>
    public const int ChampionshipWeek = 15;

    /// <summary>The last playable week.</summary>
    public const int LastRegularWeek = ChampionshipWeek - 1;

    /// <summary>How long the week under test, and every week after it, lasts.</summary>
    public static readonly TimeSpan WeekLength = TimeSpan.FromDays(7);

    /// <summary>How long each week before the week under test is laid out as.</summary>
    public static readonly TimeSpan PastWeekLength = TimeSpan.FromDays(1);

    /// <summary>
    /// How far before now the simulator starts the week it enters (D-190): three days, so the
    /// week's picks-open moment (Tuesday 04:00 ET of the window, Feature 16) has passed and the
    /// tester can pick at once. The week then has four days left to run.
    /// </summary>
    public static readonly TimeSpan PicksOpenLead = TimeSpan.FromDays(3);

    /// <summary>
    /// The gap between one window's inclusive end and the next one's start, matching the real
    /// calendar's Saturday 23:59:59.999 to Sunday 00:00:00.000.
    /// </summary>
    public static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(1);

    /// <summary>The whole season with <paramref name="currentWeek"/> starting at <paramref name="currentStartUtc"/>.</summary>
    /// <param name="seasonYear">The simulated season.</param>
    /// <param name="currentWeek">The week under test, <see cref="FirstWeek"/>..<see cref="ChampionshipWeek"/>.</param>
    /// <param name="currentStartUtc">When that week's window opens.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="currentWeek"/> is not in the season.</exception>
    public static IReadOnlyList<SeasonWeek> Build(int seasonYear, int currentWeek, DateTimeOffset currentStartUtc)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(currentWeek, FirstWeek);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(currentWeek, ChampionshipWeek);

        DateTimeOffset start = currentStartUtc.ToUniversalTime();
        var weeks = new SeasonWeek[ChampionshipWeek - FirstWeek + 1];

        for (int week = FirstWeek; week <= ChampionshipWeek; week++)
        {
            (DateTimeOffset weekStart, TimeSpan length) = week switch
            {
                _ when week < currentWeek => (start - (PastWeekLength * (currentWeek - week)), PastWeekLength),
                _ when week == currentWeek => (start, WeekLength),
                _ => (start + (WeekLength * (week - currentWeek)), WeekLength),
            };

            weeks[week - FirstWeek] = new SeasonWeek(
                seasonYear,
                week,
                weekStart,
                weekStart + length - Gap,
                IsRegularSeason: week < ChampionshipWeek);
        }

        return weeks;
    }

    /// <summary>A season whose first week opens a day from now: the app reads "before the season".</summary>
    /// <param name="seasonYear">The simulated season.</param>
    /// <param name="nowUtc">Real now.</param>
    public static IReadOnlyList<SeasonWeek> BeforeSeason(int seasonYear, DateTimeOffset nowUtc) =>
        Build(seasonYear, FirstWeek, nowUtc + TimeSpan.FromDays(1));

    /// <summary>
    /// A season whose last regular week closed a minute ago: the app reads "season over" and every
    /// league in it is complete.
    /// </summary>
    /// <param name="seasonYear">The simulated season.</param>
    /// <param name="nowUtc">Real now.</param>
    public static IReadOnlyList<SeasonWeek> SeasonOver(int seasonYear, DateTimeOffset nowUtc) =>
        Build(seasonYear, LastRegularWeek, nowUtc - WeekLength - TimeSpan.FromMinutes(1));
}
