namespace NcaafPickEm.Shared.Contracts.Dev;

/// <summary>
/// Body of <c>GET</c>/<c>PUT</c>/<c>DELETE /api/admin/fixture/clock</c> (P10-01): what the server
/// currently believes "now" is, and how far that is from real time. Development and Testing only.
/// </summary>
/// <param name="NowUtc">The app clock's reading.</param>
/// <param name="RealNowUtc">The wall clock, for comparison.</param>
/// <param name="Offset">
/// <see cref="NowUtc"/> minus <see cref="RealNowUtc"/>. Grows more negative every real second while
/// the clock is frozen.
/// </param>
/// <param name="IsFrozen">True while the app clock stands still.</param>
/// <param name="IsShifted">True when the clock is frozen or offset; false when it reads real time.</param>
/// <param name="NowEasternDisplay">
/// <see cref="NowUtc"/> rendered in the league's Eastern time, e.g. "Sat 12:00 PM ET", the same
/// way lock times are shown.
/// </param>
/// <param name="CurrentWeek">
/// The week the fixture season calendar is on at <see cref="NowUtc"/>, or null when no calendar
/// is known.
/// </param>
/// <param name="SeasonState">
/// "BeforeSeason", "InSeason" or "SeasonOver" at <see cref="NowUtc"/>; null with
/// <see cref="CurrentWeek"/>.
/// </param>
public sealed record DevClockResponse(
    DateTimeOffset NowUtc,
    DateTimeOffset RealNowUtc,
    TimeSpan Offset,
    bool IsFrozen,
    bool IsShifted,
    string NowEasternDisplay,
    int? CurrentWeek,
    string? SeasonState);
