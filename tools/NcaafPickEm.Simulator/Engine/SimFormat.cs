using System.Globalization;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>How the simulator writes times and lines in its log and on its dashboard.</summary>
public static class SimFormat
{
    /// <summary>A stored UTC <see cref="DateTime"/> as an instant.</summary>
    public static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>"Tue 3:10 PM ET" - the Eastern rendering the app puts next to a lock.</summary>
    public static string Eastern(DateTimeOffset instant) => SeasonCalendar.EasternDisplay(instant);

    /// <summary>"Tue Oct 6, 3:10 PM ET" - for instants that may be days away, where the weekday alone misleads.</summary>
    public static string EasternDate(DateTimeOffset instant) =>
        SeasonCalendar.ToEastern(instant).ToString("ddd MMM d, h:mm tt", CultureInfo.InvariantCulture) + " ET";

    /// <summary>"3:10:05 PM" in this machine's time zone - the tester's, since the simulator runs locally.</summary>
    public static string Local(DateTimeOffset instant) =>
        instant.ToLocalTime().ToString("h:mm:ss tt", CultureInfo.InvariantCulture);

    /// <summary>"in 12 min", "3 min ago", "in 1 h 5 min", "now".</summary>
    public static string Relative(DateTimeOffset instant, DateTimeOffset nowUtc)
    {
        TimeSpan delta = instant - nowUtc;
        if (delta.Duration() < TimeSpan.FromSeconds(30))
        {
            return "now";
        }

        return delta > TimeSpan.Zero ? $"in {Duration(delta)}" : $"{Duration(delta)} ago";
    }

    /// <summary>"45 s", "12 min", "1 h 5 min", "2 d 3 h" - the size of a span, sign ignored.</summary>
    public static string Duration(TimeSpan span)
    {
        TimeSpan size = span.Duration();
        return size switch
        {
            _ when size < TimeSpan.FromMinutes(1) => $"{(int)size.TotalSeconds} s",
            _ when size < TimeSpan.FromHours(1) => $"{(int)size.TotalMinutes} min",
            _ when size < TimeSpan.FromDays(1) => $"{(int)size.TotalHours} h {size.Minutes} min",
            _ => $"{(int)size.TotalDays} d {size.Hours} h",
        };
    }

    /// <summary>"MICH -7.5", "Pick 'em", or "no line" - favourite first, as the picks page shows it.</summary>
    /// <param name="spread">Home spread, negative = home favoured.</param>
    /// <param name="home">Home team's short name.</param>
    /// <param name="away">Away team's short name.</param>
    public static string Spread(decimal? spread, string home, string away) => spread switch
    {
        null => "no line",
        0m => "Pick 'em",
        < 0m => string.Create(CultureInfo.InvariantCulture, $"{home} {spread.Value:0.#}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{away} {-spread.Value:0.#}"),
    };
}
