using System.Globalization;

namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// Every Eastern-time rule in the product: week windows, what counts as a Saturday game, which
/// week is current, and the default league range (04-Domain-Algorithms.md section 1, Feature 13).
/// Nothing else in the codebase may convert between UTC and Eastern.
/// </summary>
/// <remarks>
/// All storage and all arguments are UTC. The instance members read the clock through
/// <see cref="TimeProvider"/>; the static members are pure and take the instant explicitly, which
/// is what the tests use.
/// </remarks>
/// <param name="timeProvider">The only clock the calendar may read (05-Conventions.md).</param>
public sealed class SeasonCalendar(TimeProvider timeProvider)
{
    private readonly TimeProvider _timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// The league's time zone. .NET resolves the IANA id on Windows and Linux alike.
    /// </summary>
    public static TimeZoneInfo Eastern { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>Now, in UTC, from the injected clock.</summary>
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <summary>Converts an instant to Eastern, DST-aware.</summary>
    /// <param name="utc">An instant. Its own offset is respected.</param>
    public static DateTimeOffset ToEastern(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Eastern);

    /// <summary>Converts a wall-clock Eastern time to the instant it denotes.</summary>
    /// <param name="easternLocal">
    /// A wall-clock Eastern date and time. Its <see cref="DateTime.Kind"/> is ignored and treated
    /// as Eastern.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The local time does not exist (the hour skipped by the spring-forward transition). Week
    /// boundaries - midnight and one millisecond before midnight - are never in that hour.
    /// </exception>
    public static DateTimeOffset ToUtc(DateTime easternLocal)
    {
        DateTime unspecified = DateTime.SpecifyKind(easternLocal, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, Eastern), TimeSpan.Zero);
    }

    /// <summary>
    /// True when the kickoff falls on a Saturday in Eastern time. A Friday 11:30 PM Pacific
    /// kickoff is Saturday 2:30 AM Eastern and counts (Features 02 and 13).
    /// </summary>
    /// <param name="kickoffUtc">Scheduled kickoff instant.</param>
    public static bool IsSaturdayEastern(DateTimeOffset kickoffUtc) =>
        ToEastern(kickoffUtc).DayOfWeek == DayOfWeek.Saturday;

    /// <summary>
    /// The window of the week a given Saturday belongs to: the preceding Sunday 00:00:00 ET
    /// through that Saturday 23:59:59.999 ET, both converted DST-aware.
    /// </summary>
    /// <param name="saturdayEastern">The Eastern calendar date of the week's Saturday.</param>
    /// <exception cref="ArgumentException"><paramref name="saturdayEastern"/> is not a Saturday.</exception>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) WeekWindow(DateOnly saturdayEastern)
    {
        if (saturdayEastern.DayOfWeek != DayOfWeek.Saturday)
        {
            throw new ArgumentException(
                $"Week windows are anchored on a Saturday; {saturdayEastern:yyyy-MM-dd} is a {saturdayEastern.DayOfWeek}.",
                nameof(saturdayEastern));
        }

        DateOnly sunday = saturdayEastern.AddDays(-6);
        DateTimeOffset start = ToUtc(sunday.ToDateTime(TimeOnly.MinValue));
        DateTimeOffset end = ToUtc(saturdayEastern.ToDateTime(new TimeOnly(23, 59, 59, 999)));
        return (start, end);
    }

    /// <summary>
    /// The week the season is on at a given instant: the week whose window contains it; the first
    /// week before the season starts; the last regular-season week once that week's window closed.
    /// </summary>
    /// <param name="nowUtc">The instant to resolve.</param>
    /// <param name="weeks">Every week of the season.</param>
    /// <exception cref="ArgumentException"><paramref name="weeks"/> is empty.</exception>
    public static CurrentWeek CurrentWeekAt(DateTimeOffset nowUtc, IReadOnlyList<SeasonWeek> weeks)
    {
        IReadOnlyList<SeasonWeek> ordered = Ordered(weeks);

        SeasonWeek first = ordered[0];
        if (nowUtc < first.StartUtc)
        {
            return new CurrentWeek(first.Week, SeasonState.BeforeSeason);
        }

        SeasonWeek lastRegular = LastRegularSeasonWeek(ordered);
        if (nowUtc > lastRegular.EndUtc)
        {
            return new CurrentWeek(lastRegular.Week, SeasonState.SeasonOver);
        }

        SeasonWeek? containing = ordered.FirstOrDefault(w => w.Contains(nowUtc));
        if (containing is not null)
        {
            return new CurrentWeek(containing.Week, SeasonState.InSeason);
        }

        // Windows are contiguous in practice. If a source ever leaves a gap, stay on the week
        // that has already started rather than inventing one.
        SeasonWeek started = ordered.Last(w => w.StartUtc <= nowUtc);
        return new CurrentWeek(started.Week, SeasonState.InSeason);
    }

    /// <summary>The week the season is on right now.</summary>
    /// <param name="weeks">Every week of the season.</param>
    public CurrentWeek CurrentWeekNow(IReadOnlyList<SeasonWeek> weeks) => CurrentWeekAt(UtcNow, weeks);

    /// <summary>
    /// The range a new league falls back to when the season has no calendar at all: Weeks 1
    /// through 14, the length of a normal FBS regular season (D-165).
    /// </summary>
    /// <remarks>
    /// Used only by league creation on a season whose calendar has not been ingested yet - the
    /// state a freshly deployed instance is in for the first minute, before
    /// <c>ReferenceDataBootstrap</c> lands the real weeks. It is what the create-league page has
    /// always promised in that state ("Weeks will default to 1..14"), so the page and the API now
    /// agree instead of the API refusing the request.
    /// </remarks>
    public static LeagueWeekRange DefaultLeagueRangeWithoutCalendar { get; } = new(1, 14);

    /// <summary>
    /// The range a new league defaults to: Week 1 (never Week 0) through the final regular-season
    /// week, so conference championship week is excluded (Feature 13).
    /// </summary>
    /// <param name="weeks">Every week of the season.</param>
    public static LeagueWeekRange DefaultLeagueRange(IReadOnlyList<SeasonWeek> weeks)
    {
        IReadOnlyList<SeasonWeek> ordered = Ordered(weeks);
        SeasonWeek lastRegular = LastRegularSeasonWeek(ordered);

        // A season that somehow has no Week 1 starts at its earliest regular week instead.
        int firstRegular = ordered.Where(w => w.IsRegularSeason).Min(w => w.Week);
        int first = Math.Max(1, firstRegular);

        return new LeagueWeekRange(Math.Min(first, lastRegular.Week), lastRegular.Week);
    }

    /// <summary>
    /// Clamps a week a commissioner asked for into the regular season. Week 0 is allowed here -
    /// it is only excluded from the <em>default</em> range - but championship week is not.
    /// </summary>
    /// <param name="week">The requested week.</param>
    /// <param name="weeks">Every week of the season.</param>
    public static int ClampToRegularSeason(int week, IReadOnlyList<SeasonWeek> weeks)
    {
        IReadOnlyList<SeasonWeek> ordered = Ordered(weeks);
        int lowest = ordered.Where(w => w.IsRegularSeason).Min(w => w.Week);
        int highest = LastRegularSeasonWeek(ordered).Week;
        return Math.Clamp(week, lowest, highest);
    }

    /// <summary>
    /// True once the league's last week has finished: now is past that week's window
    /// (04-Domain-Algorithms.md section 1). The season leaderboard is then final.
    /// </summary>
    /// <param name="nowUtc">The instant to test.</param>
    /// <param name="lastWeek">The league's last week.</param>
    /// <param name="weeks">Every week of the season.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lastWeek"/> is not in the season.</exception>
    public static bool LeagueIsCompleteAt(DateTimeOffset nowUtc, int lastWeek, IReadOnlyList<SeasonWeek> weeks)
    {
        SeasonWeek week = Ordered(weeks).FirstOrDefault(w => w.Week == lastWeek)
            ?? throw new ArgumentOutOfRangeException(
                nameof(lastWeek),
                lastWeek,
                "The league's last week is not part of this season's calendar.");

        return nowUtc > week.EndUtc;
    }

    /// <summary>True when the league's last week has finished as of now.</summary>
    /// <param name="lastWeek">The league's last week.</param>
    /// <param name="weeks">Every week of the season.</param>
    public bool LeagueIsComplete(int lastWeek, IReadOnlyList<SeasonWeek> weeks) =>
        LeagueIsCompleteAt(UtcNow, lastWeek, weeks);

    /// <summary>
    /// The Eastern hint shown next to a lock time, e.g. "Sat 12:00 PM ET". Members see their own
    /// device time zone everywhere else; this string exists so a Mountain-time member is not
    /// confused by an Eastern lock (Feature 13).
    /// </summary>
    /// <param name="utc">The instant to render.</param>
    public static string EasternDisplay(DateTimeOffset utc) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{ToEastern(utc):ddd h:mm tt} ET");

    private static IReadOnlyList<SeasonWeek> Ordered(IReadOnlyList<SeasonWeek> weeks)
    {
        ArgumentNullException.ThrowIfNull(weeks);
        if (weeks.Count == 0)
        {
            throw new ArgumentException("The season has no weeks.", nameof(weeks));
        }

        return weeks.OrderBy(w => w.Week).ToArray();
    }

    private static SeasonWeek LastRegularSeasonWeek(IReadOnlyList<SeasonWeek> ordered) =>
        ordered.LastOrDefault(w => w.IsRegularSeason)
        ?? throw new ArgumentException("The season has no regular-season weeks.", nameof(ordered));
}
