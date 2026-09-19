using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Leaderboard;
using NcaafPickEm.Shared.Contracts.Reference;
using NcaafPickEm.Shared.Contracts.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// In-memory <see cref="ILeaderboardApi"/> for Development, used until P5-03's real endpoints
/// merge (see DECISIONS.md for the fake-API switch, D-043/D-044). Reuses
/// <see cref="FakeLeaguesApi.SampleLeagueId"/> and the same five member ids
/// (Michael/Alyson/Dance/Alex/Daniel) so the fake describes one consistent session, without a
/// shared store between the two classes.
/// </summary>
/// <remarks>
/// Seeded story, three weeks: <b>Week 5</b> and <b>Week 6</b> are Complete with distinct weekly
/// winners (Michael, then Alyson); <b>Dance</b> played week 5 only and has since been removed
/// (a former member, excluded from the season leaderboard but shown, marked, in week 5's rows and
/// grid); <b>Alex</b> joined at week 6 (a late joiner, no rank to compare against for week 5, so
/// their season trend is <see cref="StandingsTrend.None"/>). Season trend after week 6 covers all
/// four states: Alyson <see cref="StandingsTrend.Up"/>, Michael <see cref="StandingsTrend.Down"/>,
/// Daniel <see cref="StandingsTrend.Same"/>, Alex <see cref="StandingsTrend.None"/>. <b>Week 7</b>
/// is In Progress (partial results) with a 12-game grid covering every
/// <see cref="GridOutcome"/>: three Final games with a determined winner (Correct/Incorrect
/// split across members), a Final tie (Pending — "needs review", no winner), a live in-progress
/// game (Pending), a not-yet-started game (Pending), a commissioner-voided cancellation
/// (Voided, greyed row), and at least one member skipping a pick on a decided game (NoPick).
/// <b>Week 8</b> has a generated game set that has not yet locked, for the grid's pre-lock state
/// ("Grid opens at kickoff"). The caller is always Michael (the commissioner), matching
/// <see cref="FakeLeaguesApi"/>'s default caller.
/// </remarks>
public sealed class FakeLeaderboardApi : ILeaderboardApi
{
    private static readonly Guid MichaelId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AlysonId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid DanceId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid AlexId = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid DanielId = Guid.Parse("00000000-0000-0000-0000-000000000005");

    /// <summary>The league's current week, matching <see cref="FakeLeaguesApi"/>'s sample league.</summary>
    public const int CurrentWeek = 7;

    private const int FirstWeek = 1;
    private const int LastWeek = 14;

    /// <summary>
    /// <c>GET /api/leagues/{leagueId}/weeks</c>'s answer for the sample league, shared with
    /// <see cref="FakeLeaguesApi.GetLeagueWeeksAsync"/> so both fakes agree on which weeks are
    /// navigable.
    /// </summary>
    public static LeagueWeek[] BuildLeagueWeeks()
    {
        LeagueWeek[] weeks = new LeagueWeek[LastWeek - FirstWeek + 1];
        for (int week = FirstWeek; week <= LastWeek; week++)
        {
            weeks[week - FirstWeek] = week switch
            {
                5 => new LeagueWeek(5, HasGameSet: true, IsCurrent: false, IsLocked: true, IsComplete: true, LockAtUtc(5)),
                6 => new LeagueWeek(6, HasGameSet: true, IsCurrent: false, IsLocked: true, IsComplete: true, LockAtUtc(6)),
                7 => new LeagueWeek(7, HasGameSet: true, IsCurrent: true, IsLocked: true, IsComplete: false, LockAtUtc(7)),
                8 => new LeagueWeek(8, HasGameSet: true, IsCurrent: false, IsLocked: false, IsComplete: false, LockAtUtc(8)),
                _ => new LeagueWeek(week, HasGameSet: false, IsCurrent: false, IsLocked: false, IsComplete: false, null),
            };
        }

        return weeks;
    }

    private static DateTimeOffset LockAtUtc(int week) =>
        new DateTimeOffset(2026, 9, 26, 16, 0, 0, TimeSpan.Zero).AddDays((week - 4) * 7); // Saturdays, 12:00 PM ET.

    /// <inheritdoc />
    public Task<SeasonLeaderboard> GetSeasonLeaderboardAsync(
        Guid leagueId, CancellationToken cancellationToken = default)
    {
        RequireSampleLeague(leagueId);

        SeasonRow[] rows =
        [
            new(Rank: 1, AlysonId, "Alyson", TotalPoints: 70, PointsBehind: 0, WeeklyWins: 1, StandingsTrend.Up, IsMe: false),
            new(Rank: 2, MichaelId, "Michael", TotalPoints: 35, PointsBehind: 35, WeeklyWins: 1, StandingsTrend.Down, IsMe: true),
            new(Rank: 3, DanielId, "Daniel", TotalPoints: 18, PointsBehind: 52, WeeklyWins: 0, StandingsTrend.Same, IsMe: false),
            new(Rank: 4, AlexId, "Alex", TotalPoints: 12, PointsBehind: 58, WeeklyWins: 0, StandingsTrend.None, IsMe: false),
        ];

        return Task.FromResult(new SeasonLeaderboard(ThroughWeek: 6, rows));
    }

    /// <inheritdoc />
    public Task<WeekLeaderboard> GetWeekLeaderboardAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        RequireSampleLeague(leagueId);

        WeekRow[] rows = week switch
        {
            5 =>
            [
                new(Rank: 1, MichaelId, "Michael", Points: 30, Correct: 8, Total: 10, IsWinner: true, IsFormer: false, IsMe: true),
                new(Rank: 2, AlysonId, "Alyson", Points: 20, Correct: 6, Total: 10, IsWinner: false, IsFormer: false, IsMe: false),
                new(Rank: 3, DanceId, "Dance", Points: 15, Correct: 5, Total: 10, IsWinner: false, IsFormer: true, IsMe: false),
                new(Rank: 4, DanielId, "Daniel", Points: 10, Correct: 3, Total: 10, IsWinner: false, IsFormer: false, IsMe: false),
            ],
            6 =>
            [
                new(Rank: 1, AlysonId, "Alyson", Points: 50, Correct: 9, Total: 10, IsWinner: true, IsFormer: false, IsMe: false),
                new(Rank: 2, AlexId, "Alex", Points: 12, Correct: 4, Total: 10, IsWinner: false, IsFormer: false, IsMe: false),
                new(Rank: 3, DanielId, "Daniel", Points: 8, Correct: 3, Total: 10, IsWinner: false, IsFormer: false, IsMe: false),
                new(Rank: 4, MichaelId, "Michael", Points: 5, Correct: 2, Total: 10, IsWinner: false, IsFormer: false, IsMe: true),
            ],
            7 =>
            [
                new(Rank: 1, MichaelId, "Michael", Points: 30, Correct: 6, Total: 11, IsWinner: false, IsFormer: false, IsMe: true),
                new(Rank: 1, AlexId, "Alex", Points: 30, Correct: 6, Total: 11, IsWinner: false, IsFormer: false, IsMe: false),
                new(Rank: 3, AlysonId, "Alyson", Points: 25, Correct: 5, Total: 11, IsWinner: false, IsFormer: false, IsMe: false),
                new(Rank: 3, DanielId, "Daniel", Points: 25, Correct: 5, Total: 11, IsWinner: false, IsFormer: false, IsMe: false),
            ],
            _ => [],
        };

        bool isComplete = week is 5 or 6;
        return Task.FromResult(new WeekLeaderboard(week, isComplete, rows));
    }

    /// <inheritdoc />
    public Task<WeekGrid> GetWeekGridAsync(Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        RequireSampleLeague(leagueId);

        LeagueWeek[] weeks = BuildLeagueWeeks();
        LeagueWeek? row = weeks.FirstOrDefault(w => w.Week == week);
        if (row is null || !row.HasGameSet)
        {
            throw new LeaguesApiException(404, "WeekOutOfRange");
        }

        if (!row.IsLocked)
        {
            throw new LeaguesApiException(403, "PicksNotVisible");
        }

        return Task.FromResult(week switch
        {
            5 => BuildSimpleGrid(week: 5, gameCount: 10, [(MichaelId, "Michael", false), (AlysonId, "Alyson", false), (DanceId, "Dance", true), (DanielId, "Daniel", false)]),
            6 => BuildSimpleGrid(week: 6, gameCount: 10, [(MichaelId, "Michael", false), (AlysonId, "Alyson", false), (AlexId, "Alex", false), (DanielId, "Daniel", false)]),
            7 => BuildWeek7Grid(),
            _ => throw new LeaguesApiException(404, "WeekOutOfRange"),
        });
    }

    private static void RequireSampleLeague(Guid leagueId)
    {
        if (leagueId != FakeLeaguesApi.SampleLeagueId)
        {
            throw new LeaguesApiException(404, "League not found.");
        }
    }

    // A pool of 12 real-school matchups, reused (with different results) across weeks so the
    // fake does not need a second 24-team roster per week.
    private static readonly (string Away, string Home)[] Matchups =
    [
        ("Michigan", "Texas"),
        ("Maryland", "Rutgers"),
        ("Ohio State", "Wisconsin"),
        ("Alabama", "Auburn"),
        ("Georgia", "Kentucky"),
        ("Oklahoma", "Missouri"),
        ("Clemson", "Florida State"),
        ("Iowa State", "Kansas"),
        ("TCU", "Baylor"),
        ("Penn State", "Indiana"),
        ("Boise State", "Fresno State"),
        ("USC", "Stanford"),
    ];

    private static readonly Dictionary<string, string> Abbreviations = new()
    {
        ["Michigan"] = "MICH",
        ["Texas"] = "TEX",
        ["Maryland"] = "MD",
        ["Rutgers"] = "RUTG",
        ["Ohio State"] = "OSU",
        ["Wisconsin"] = "WIS",
        ["Alabama"] = "ALA",
        ["Auburn"] = "AUB",
        ["Georgia"] = "UGA",
        ["Kentucky"] = "UK",
        ["Oklahoma"] = "OU",
        ["Missouri"] = "MIZ",
        ["Clemson"] = "CLEM",
        ["Florida State"] = "FSU",
        ["Iowa State"] = "ISU",
        ["Kansas"] = "KU",
        ["TCU"] = "TCU",
        ["Baylor"] = "BAY",
        ["Penn State"] = "PSU",
        ["Indiana"] = "IND",
        ["Boise State"] = "BSU",
        ["Fresno State"] = "FRES",
        ["USC"] = "USC",
        ["Stanford"] = "STAN",
    };

    private static TeamDto Team(string school)
    {
        // Deterministic, collision-free within this small pool: an 8-hex-digit hash of the
        // school name padded to the 12 hex digits the last GUID group needs.
        uint hash = unchecked((uint)school.GetHashCode());
        Guid teamId = Guid.Parse($"00000000-0000-0000-0003-{hash:x8}0000");
        return new TeamDto(teamId, school, Abbreviations.GetValueOrDefault(school, school), ConferenceId: null, LogoUrl: null);
    }

    private static WeekGrid BuildSimpleGrid(int week, int gameCount, (Guid Id, string Name, bool IsFormer)[] members)
    {
        DateTimeOffset kickoff = new DateTimeOffset(2026, 9, 26, 16, 0, 0, TimeSpan.Zero).AddDays((week - 4) * 7);

        var games = new GameSetGameDto[gameCount];
        var cells = new List<GridCell>();

        for (int i = 0; i < gameCount; i++)
        {
            (string awayName, string homeName) = Matchups[i];
            TeamDto away = Team(awayName);
            TeamDto home = Team(homeName);
            Guid gameSetGameId = Guid.Parse($"00000000-0000-0000-{week:x4}-{i:x12}");
            bool homeWins = i % 2 == 0;
            Guid winnerId = homeWins ? home.TeamId : away.TeamId;

            games[i] = new GameSetGameDto(
                GameSetGameId: gameSetGameId,
                GameId: gameSetGameId,
                HomeTeam: home,
                AwayTeam: away,
                HomeRank: null,
                AwayRank: null,
                KickoffUtc: kickoff.AddHours(i),
                PointValue: 10,
                IsPointValueElevated: false,
                Source: GameSetGameSource.Rule,
                Status: GameStatus.Final,
                HomeScore: homeWins ? 24 : 17,
                AwayScore: homeWins ? 17 : 24,
                Period: null,
                Clock: null,
                IsVoided: false,
                WinnerTeamId: winnerId);

            for (int m = 0; m < members.Length; m++)
            {
                int slot = (i + m) % 3;
                Guid? pick = slot switch
                {
                    0 => winnerId, // Correct.
                    1 => homeWins ? away.TeamId : home.TeamId, // Incorrect.
                    _ => null, // NoPick.
                };
                GridOutcome outcome = slot switch
                {
                    0 => GridOutcome.Correct,
                    1 => GridOutcome.Incorrect,
                    _ => GridOutcome.NoPick,
                };
                cells.Add(new GridCell(gameSetGameId, members[m].Id, pick, outcome));
            }
        }

        GridMember[] gridMembers = [.. members.Select(m => new GridMember(m.Id, m.Name, m.IsFormer))];
        return new WeekGrid(games, gridMembers, [.. cells]);
    }

    private static WeekGrid BuildWeek7Grid()
    {
        DateTimeOffset saturday = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

        (Guid Id, string Name)[] members =
        [
            (MichaelId, "Michael"),
            (AlysonId, "Alyson"),
            (DanielId, "Daniel"),
            (AlexId, "Alex"),
        ];

        // (Status, IsVoided, HomeScore, AwayScore, WinnerIsHome?) per matchup, in Matchups order
        // (first 12 entries). WinnerIsHome is null when there is no determinable winner yet.
        (GameStatus Status, bool Voided, int? HomeScore, int? AwayScore, bool? WinnerIsHome)[] results =
        [
            (GameStatus.Final, false, 31, 24, true),    // 1 Michigan @ Texas: Texas (home) wins.
            (GameStatus.Final, false, 17, 20, false),   // 2 Maryland @ Rutgers: Maryland (away) wins.
            (GameStatus.Final, false, 10, 35, false),   // 3 Ohio State @ Wisconsin: Ohio State (away) wins.
            (GameStatus.Final, false, 27, 27, null),    // 4 Alabama @ Auburn: tie, needs review.
            (GameStatus.Final, false, 3, 45, false),    // 5 Georgia @ Kentucky: Georgia (away) wins.
            (GameStatus.InProgress, false, 10, 14, null), // 6 Oklahoma @ Missouri: still playing.
            (GameStatus.Cancelled, true, null, null, null), // 7 Clemson @ Florida State: voided.
            (GameStatus.Final, false, 20, 30, false),   // 8 Iowa State @ Kansas: Iowa State (away) wins.
            (GameStatus.Scheduled, false, null, null, null), // 9 TCU @ Baylor: not started.
            (GameStatus.Final, false, 6, 40, false),    // 10 Penn State @ Indiana: Penn State (away) wins.
            (GameStatus.Final, false, 14, 21, false),   // 11 Boise State @ Fresno State: Boise State (away) wins.
            (GameStatus.Final, false, 16, 17, false),   // 12 USC @ Stanford: USC (away) wins.
        ];

        // Each member's pick per game: H = home team, A = away team, N = no pick.
        char[][] picks =
        [
            // Michael
            ['H', 'A', 'H', 'H', 'A', 'A', 'H', 'A', 'H', 'A', 'H', 'A'],
            // Alyson
            ['A', 'A', 'A', 'A', 'A', 'H', 'A', 'H', 'H', 'A', 'A', 'H'],
            // Daniel
            ['H', 'H', 'A', 'H', 'H', 'A', 'N', 'A', 'A', 'N', 'A', 'A'],
            // Alex
            ['H', 'A', 'N', 'A', 'A', 'N', 'H', 'A', 'N', 'H', 'A', 'A'],
        ];

        var games = new GameSetGameDto[Matchups.Length];
        var cells = new List<GridCell>();

        for (int i = 0; i < Matchups.Length; i++)
        {
            (string awayName, string homeName) = Matchups[i];
            TeamDto away = Team(awayName);
            TeamDto home = Team(homeName);
            Guid gameSetGameId = Guid.Parse($"00000000-0000-0000-0007-{i:x12}");
            (GameStatus status, bool voided, int? homeScore, int? awayScore, bool? winnerIsHome) = results[i];
            Guid? winnerId = winnerIsHome switch
            {
                true => home.TeamId,
                false => away.TeamId,
                null => null,
            };

            games[i] = new GameSetGameDto(
                GameSetGameId: gameSetGameId,
                GameId: gameSetGameId,
                HomeTeam: home,
                AwayTeam: away,
                HomeRank: null,
                AwayRank: null,
                KickoffUtc: saturday.AddHours(12 + i),
                PointValue: 10,
                IsPointValueElevated: false,
                Source: GameSetGameSource.Rule,
                Status: status,
                HomeScore: homeScore,
                AwayScore: awayScore,
                Period: status == GameStatus.InProgress ? (byte)3 : null,
                Clock: status == GameStatus.InProgress ? "8:14" : null,
                IsVoided: voided,
                WinnerTeamId: winnerId);

            for (int m = 0; m < members.Length; m++)
            {
                char mark = picks[m][i];
                Guid? teamId = mark switch
                {
                    'H' => home.TeamId,
                    'A' => away.TeamId,
                    _ => null,
                };

                GridOutcome outcome;
                if (voided)
                {
                    outcome = GridOutcome.Voided;
                }
                else if (teamId is null)
                {
                    outcome = GridOutcome.NoPick;
                }
                else if (winnerId is null)
                {
                    outcome = GridOutcome.Pending;
                }
                else
                {
                    outcome = teamId == winnerId ? GridOutcome.Correct : GridOutcome.Incorrect;
                }

                cells.Add(new GridCell(gameSetGameId, members[m].Id, teamId, outcome));
            }
        }

        GridMember[] gridMembers = [.. members.Select(m => new GridMember(m.Id, m.Name, IsFormer: false))];
        return new WeekGrid(games, gridMembers, [.. cells]);
    }
}
