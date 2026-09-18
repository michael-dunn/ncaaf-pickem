namespace NcaafPickEm.Domain.Tests.GameSets;

/// <summary>
/// Fixed instants for the generator tests. The week is 2026 week 7, whose Saturday is
/// 2026-10-17 Eastern. Nothing here is converted at runtime: <c>IsSaturdayEastern</c> is an
/// ingest-time flag the generator only reads, so these values are documentation of what each
/// flag means in the real world.
/// </summary>
internal static class GameSetTimes
{
    /// <summary>Saturday 2026-10-17, 12:00 PM Eastern.</summary>
    public static readonly DateTime SaturdayNoon = new(2026, 10, 17, 16, 0, 0, DateTimeKind.Utc);

    /// <summary>Saturday 2026-10-17, 3:30 PM Eastern.</summary>
    public static readonly DateTime SaturdayAfternoon = new(2026, 10, 17, 19, 30, 0, DateTimeKind.Utc);

    /// <summary>Saturday 2026-10-17, 8:00 PM Eastern.</summary>
    public static readonly DateTime SaturdayNight = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Friday 2026-10-16, 9:30 PM Pacific - Saturday 12:30 AM Eastern, so it counts.</summary>
    public static readonly DateTime FridayNightPacific = new(2026, 10, 17, 4, 30, 0, DateTimeKind.Utc);

    /// <summary>Friday 2026-10-16, 7:00 PM Eastern - a Friday game, so it never counts.</summary>
    public static readonly DateTime FridayNightEastern = new(2026, 10, 16, 23, 0, 0, DateTimeKind.Utc);

    /// <summary>The Tuesday the week 7 AP poll was fetched.</summary>
    public static readonly DateTime PollFetchedTuesday = new(2026, 10, 13, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>An earlier fetch of the same week's poll, superseded by the Tuesday one.</summary>
    public static readonly DateTime PollFetchedSunday = new(2026, 10, 11, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>When the previous week's poll was fetched.</summary>
    public static readonly DateTime PriorPollFetched = new(2026, 10, 6, 18, 0, 0, DateTimeKind.Utc);
}
