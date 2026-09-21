namespace NcaafPickEm.Shared.Contracts.Dev;

/// <summary>
/// Body of every <c>/api/admin/fixture/demo</c> route (P10-01): where the seeded demo league is in
/// the week the clock currently points at, plus what the control just did. Development and
/// Testing only.
/// </summary>
/// <param name="LeagueId">The demo league ("Family League").</param>
/// <param name="LeagueName">Its name.</param>
/// <param name="NowUtc">The app clock's reading when the response was built.</param>
/// <param name="CurrentWeek">
/// The week the calendar is on at <see cref="NowUtc"/>, clamped to the league's range. Every
/// control acts on this week.
/// </param>
/// <param name="SeasonState">"BeforeSeason", "InSeason" or "SeasonOver" at <see cref="NowUtc"/>.</param>
/// <param name="HasFixtureGames">
/// True when the fixture data set has games in <see cref="CurrentWeek"/>. The fixture week is the
/// only week with games, so generate does nothing useful anywhere else.
/// </param>
/// <param name="Snapshot">The fixture live-score snapshot the poller currently reads (1..6).</param>
/// <param name="Set">The week's game set, or null when none has been created yet.</param>
/// <param name="Members">Every active member, with their status and points for the week.</param>
/// <param name="Notes">
/// What the control just did or why it did nothing ("generate: 12 games", "picks: Alyson locked").
/// Empty for a plain <c>GET</c>.
/// </param>
public sealed record DemoWeekResponse(
    Guid LeagueId,
    string LeagueName,
    DateTimeOffset NowUtc,
    int CurrentWeek,
    string SeasonState,
    bool HasFixtureGames,
    int Snapshot,
    DemoWeekSetDto? Set,
    DemoMemberDto[] Members,
    string[] Notes);
