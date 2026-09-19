using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.Admin;
using NcaafPickEm.Shared.Contracts.Dashboard;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Invites;
using NcaafPickEm.Shared.Contracts.Leaderboard;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Contracts.Points;
using NcaafPickEm.Shared.Contracts.Scoring;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests.Simulation;

/// <summary>
/// P8-03: one league's whole week, end to end, against the Week 7, 2026 fixtures and a clock the
/// test moves - league creation, invites, the Tuesday regeneration job, picks, both Friday
/// reminders, the Saturday last call, the lock job, the Overview dashboards, all six score
/// snapshots, a void, an override, the completed week, and the season leaderboard, grid, snapshot
/// rows and trend arrows against a prior completed week.
/// </summary>
/// <remarks>
/// <para>
/// Its own <see cref="SqlTestDatabase"/> and <see cref="ApiFactory"/>, like
/// <c>ScoringSnapshotWalkTests</c>: advancing <see cref="FixtureSnapshotState"/> writes real
/// scores onto the shared fixture <c>Games</c> rows and must not leak into the run's shared
/// database.
/// </para>
/// <para>
/// One test method, because the whole point is the sequence: each assertion only means anything
/// against the state the step before it left behind. Everything a member or commissioner would do
/// in a browser goes through the real HTTP endpoints; everything the server would do on a timer
/// goes through the real <see cref="SchedulerTick"/> or
/// <see cref="SaturdayPoller.PollOnceAsync"/>.
/// </para>
/// <para>
/// Roster (D-200): the Overview's five worked-example names plus a sixth member, Sam, who never
/// opens the picks page. The card asks for "one leaves 2 unpicked, one never starts" while the
/// Overview needs all five named members to have picked both example games, so the sixth member
/// carries the never-started case. A member with no pick lands in the dashboard's
/// <c>NoPick</c> list, never in anyone's <c>OppositePicks</c>, so the Overview's documented
/// opposite lists still hold exactly.
/// </para>
/// </remarks>
public sealed class FullWeekSimulationTests : IAsyncLifetime
{
    // ---- The timeline, in Eastern wall-clock terms (EDT, UTC-4, all through October 2026) -----

    /// <summary>Monday 2026-10-05, 09:00 ET - inside week 6, so everyone's JoinedWeek is 6.</summary>
    private static readonly DateTimeOffset LeagueSetupUtc = new(2026, 10, 5, 13, 0, 0, TimeSpan.Zero);

    /// <summary>Tuesday 2026-10-13, 03:30 ET - <c>RegenerateGameSetsJob</c>'s cron minute.</summary>
    private static readonly DateTimeOffset TuesdayRegenerateUtc = new(2026, 10, 13, 7, 30, 0, TimeSpan.Zero);

    /// <summary>Wednesday 2026-10-14, 19:00 ET - members make their picks.</summary>
    private static readonly DateTimeOffset PickingUtc = new(2026, 10, 14, 23, 0, 0, TimeSpan.Zero);

    /// <summary>Friday 2026-10-16, 20:00 ET - <c>FridayMemberReminderJob</c>.</summary>
    private static readonly DateTimeOffset FridayReminderUtc = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Friday 2026-10-16, 21:00 ET - <c>FridayCommissionerSummaryJob</c>.</summary>
    private static readonly DateTimeOffset FridaySummaryUtc = new(2026, 10, 17, 1, 0, 0, TimeSpan.Zero);

    /// <summary>Saturday 2026-10-17, 11:00 ET - <c>SaturdayReminderOneShot</c>, one hour to lock.</summary>
    private static readonly DateTimeOffset SaturdayLastCallUtc = new(2026, 10, 17, 15, 0, 0, TimeSpan.Zero);

    /// <summary>Saturday 2026-10-17, 12:00 ET - the earliest kickoff in the set, so the lock.</summary>
    private static readonly DateTimeOffset LockUtc = new(2026, 10, 17, 16, 0, 0, TimeSpan.Zero);

    /// <summary>When each fixture score snapshot is polled, snapshot 1 first.</summary>
    private static readonly DateTimeOffset[] SnapshotInstantsUtc =
    [
        new(2026, 10, 17, 16, 5, 0, TimeSpan.Zero),
        new(2026, 10, 17, 18, 5, 0, TimeSpan.Zero),
        new(2026, 10, 17, 21, 5, 0, TimeSpan.Zero),
        new(2026, 10, 18, 0, 35, 0, TimeSpan.Zero),
        new(2026, 10, 18, 3, 35, 0, TimeSpan.Zero),
        new(2026, 10, 18, 5, 45, 0, TimeSpan.Zero),
    ];

    /// <summary>Sunday 2026-10-18, 08:00 ET - the commissioner settles the week.</summary>
    private static readonly DateTimeOffset CorrectionsUtc = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Every fixture Week 7 kickoff lands on this Eastern calendar date.</summary>
    private static readonly DateOnly FixtureSaturday = new(2026, 10, 17);

    // ---- The league's week 7 game set, by fixture CFBD id ------------------------------------

    private const long MichiganTexas = 700001;
    private const long MarylandRutgers = 700002;
    private const long AlabamaAuburn = 700006;
    private const long GeorgiaKentucky = 700007;
    private const long OregonWashington = 700010;
    private const long IowaStateKansas = 700011;
    private const long SanJoseHawaii = 700004;

    /// <summary>The three games the Top 25 rule selects on its own.</summary>
    private static readonly long[] RuleGames = [MichiganTexas, AlabamaAuburn, GeorgiaKentucky];

    /// <summary>
    /// The four the commissioner adds by hand: the Overview's second matchup, the Iowa State /
    /// Kansas tie that needs a correction, and the two late kickoffs that only go final in
    /// snapshots 5 and 6.
    /// </summary>
    private static readonly long[] ManualGames = [MarylandRutgers, IowaStateKansas, OregonWashington, SanJoseHawaii];

    private const int Week = 7;
    private const int PriorWeek = 6;
    private const int TiePointValue = 15;
    private const int StandardPointValue = 10;

    // ---- Week 6, the prior completed week: bespoke games so the poller can never touch them ---

    private const long PriorMichiganOhioState = 760001;
    private const long PriorGeorgiaAlabama = 760002;
    private const long PriorClemsonOregon = 760003;

    private readonly FakePushSender _push = new();
    private readonly FixedTimeProvider _clock = new(LeagueSetupUtc);

    private SqlTestDatabase? _database;
    private ApiFactory? _factory;

    private ApiFactory Factory => _factory
        ?? throw new InvalidOperationException("The simulation has not been initialized.");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _database = await SqlTestDatabase.CreateAsync();
        _factory = new ApiFactory(
            _database.ConnectionString,
            timeProvider: _clock,
            configureServices: services =>
            {
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(_push);

                // Every tick this test drives is aimed at one exact cron minute. A one-minute
                // catch-up window keeps a tick from also replaying the hour of refresh jobs that
                // happen to sit just before it (AGENT-NOTES "Jobs": the window is
                // Jobs:CatchUpMinutes wide and a job that has never run starts its window there).
                services.Configure<JobsOptions>(options => options.CatchUpMinutes = 1);
            });
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [Fact]
    public async Task GivenTheFixtureSeason_WhenAWholeWeekIsPlayedEndToEnd_ThenEveryStoryHoldsFromInviteToTrendArrow()
    {
        await FixtureGameData.EnsureSeededAsync(Factory);

        // === Week 6: the league is created and five people are invited into it =================

        _clock.Set(LeagueSetupUtc);

        SimulationWorld world = await CreateLeagueAndInviteMembersAsync();

        await AssertRosterAsync(world);

        // Default configuration: the Top 25 rule, saved but not generated (both gameset-rules
        // routes are save-only; the Tuesday job is what regenerates).
        using (HttpClient commissioner = Factory.CreateMutatingClientAs(world["Michael"].UserId))
        {
            GameSetRuleDto[] rules = [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)];
            using HttpResponseMessage response = await commissioner.PutAsJsonAsync(
                $"/api/leagues/{world.LeagueId}/gameset-rules", rules);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        bool anyWeekSevenSetYet = await Factory.QueryDbAsync(db => db.WeekGameSets
            .AnyAsync(set => set.LeagueId == world.LeagueId && set.Week == Week));
        anyWeekSevenSetYet.Should().BeFalse("saving rules never generates a set");

        // A completed prior week, so the season leaderboard has something to compare against and
        // the trend arrows have two snapshot weeks to work with.
        await SeedCompletedPriorWeekAsync(world);
        await AssertPriorWeekAsync(world);

        // === Tuesday 03:30 ET: the regeneration job builds week 7 ==============================

        await TickAsync(TuesdayRegenerateUtc);

        WeekGameSetResponse generated = await GetWeekGameSetAsync(world);
        generated.Games.Select(game => world.CfbdIdOf(game.GameId))
            .Should().BeEquivalentTo(RuleGames, "the Top 25 rule picks exactly the three ranked matchups");

        // The commissioner rounds the week out by hand and weights the tie game.
        using (HttpClient commissioner = Factory.CreateMutatingClientAs(world["Michael"].UserId))
        {
            foreach (long cfbdGameId in ManualGames)
            {
                using HttpResponseMessage response = await commissioner.PostAsJsonAsync(
                    $"/api/leagues/{world.LeagueId}/weeks/{Week}/gameset/games",
                    new AddGameRequest(world.GameIdOf(cfbdGameId)));
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            using HttpResponseMessage points = await commissioner.PutAsJsonAsync(
                $"/api/leagues/{world.LeagueId}/weeks/{Week}/gameset/games/{world.GameIdOf(IowaStateKansas)}/points",
                new SetPointOverrideRequest(TiePointValue));
            points.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        WeekGameSetResponse set = await GetWeekGameSetAsync(world);
        set.Games.Should().HaveCount(7);
        set.LockAtUtc.Should().Be(LockUtc, "the lock is the earliest kickoff in the set, Maryland / Rutgers at noon ET");
        set.Games.Single(game => world.CfbdIdOf(game.GameId) == IowaStateKansas).PointValue.Should().Be(TiePointValue);
        world.RememberGames(set.Games);

        // === Tuesday to Friday: the members pick ==============================================

        _clock.Set(PickingUtc);
        await MakeEveryonesPicksAsync(world);
        await AssertPickRosterAsync(world);

        using (HttpClient alyson = Factory.CreateClientAs(world["Alyson"].UserId))
        {
            using HttpResponseMessage hidden = await alyson.GetAsync(
                $"/api/leagues/{world.LeagueId}/weeks/{Week}/picks");
            hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden, "nobody sees anybody else's picks before lock");
        }

        // === Friday 20:00 ET: the member reminder =============================================

        _push.Reset();
        await TickAsync(FridayReminderUtc);

        string lockTimeEastern = SeasonCalendar.ToEastern(LockUtc).ToString("h:mm tt", CultureInfo.InvariantCulture);

        _push.SentTo(world["Alex"].PushEndpoint).Should().ContainSingle().Which
            .Payload.Body.Should().Be($"You have 2 picks left for Week 7. Lock is Saturday at {lockTimeEastern}.");
        _push.SentTo(world["Sam"].PushEndpoint).Should().ContainSingle().Which
            .Payload.Body.Should().Be($"You have 7 picks left for Week 7. Lock is Saturday at {lockTimeEastern}.");

        foreach (string name in new[] { "Michael", "Alyson", "Dance", "Daniel" })
        {
            _push.SentTo(world[name].PushEndpoint).Should().BeEmpty($"{name} has already submitted");
        }

        // === Friday 21:00 ET: the commissioner summary ========================================

        _push.Reset();
        await TickAsync(FridaySummaryUtc);

        FakePushSender.SentPush summary = _push.SentTo(world["Michael"].PushEndpoint).Should().ContainSingle().Subject;
        summary.Payload.Body.Should().StartWith("2 members haven't submitted Week 7 picks:");
        summary.Payload.Body.Should().Contain("Alex").And.Contain("Sam");
        summary.Payload.Url.Should().Be($"/leagues/{world.LeagueId}");

        foreach (string name in new[] { "Alyson", "Dance", "Alex", "Daniel", "Sam" })
        {
            _push.SentTo(world[name].PushEndpoint).Should().BeEmpty("only commissioners get the summary");
        }

        // === Saturday 11:00 ET: one hour to lock ==============================================

        _push.Reset();
        await TickAsync(SaturdayLastCallUtc);

        _push.SentTo(world["Alex"].PushEndpoint).Should().ContainSingle().Which
            .Payload.Body.Should().Be("Picks lock in 1 hour. You have 2 picks left for Week 7.");
        _push.SentTo(world["Sam"].PushEndpoint).Should().ContainSingle().Which
            .Payload.Body.Should().Be("Picks lock in 1 hour. You have 7 picks left for Week 7.");
        _push.SentTo(world["Michael"].PushEndpoint).Should().BeEmpty();

        // === Saturday 12:00 ET: the lock ======================================================

        await TickAsync(LockUtc);

        await AssertLockedStatusesAsync(world);
        await AssertPicksAreFrozenAsync(world);
        await AssertOverviewDashboardsAsync(world);

        // === Saturday into Sunday: the six score snapshots ====================================

        await WalkSnapshotsAsync(world);

        // === Sunday morning: the commissioner settles the week ================================

        _clock.Set(CorrectionsUtc);
        await VoidAndOverrideAsync(world);

        // === The closed week, the season table, the grid and the trend arrows =================

        await AssertCompletedWeekAsync(world);
        await AssertSeasonLeaderboardAsync(world);
        await AssertGridAsync(world);
        await AssertSnapshotsAndTrendsAsync(world);
        await AssertAuditTrailAsync(world);
    }

    // =========================================================================================
    // Setup
    // =========================================================================================

    private async Task<SimulationWorld> CreateLeagueAndInviteMembersAsync()
    {
        Dictionary<string, Guid> userIds = new(StringComparer.Ordinal);
        foreach (string name in SimulationWorld.MemberNames)
        {
            User user = await Factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, name));
            userIds[name] = user.Id;
        }

        Guid leagueId;
        string inviteCode;

        using (HttpClient michael = Factory.CreateMutatingClientAs(userIds["Michael"]))
        {
            using HttpResponseMessage created = await michael.PostAsJsonAsync(
                "/api/leagues", new CreateLeagueRequest("Simulation League", 2026, null, null));
            created.StatusCode.Should().Be(HttpStatusCode.OK);

            LeagueDetail? detail = await created.Content.ReadFromJsonAsync<LeagueDetail>();
            detail!.MyRole.Should().Be(MembershipRole.Commissioner, "the creator is the first commissioner");
            detail.CurrentWeek.Should().Be(PriorWeek);
            leagueId = detail.LeagueId;

            using HttpResponseMessage invited = await michael.PostAsync($"/api/leagues/{leagueId}/invites", null);
            invited.StatusCode.Should().Be(HttpStatusCode.OK);
            InviteResponse? invite = await invited.Content.ReadFromJsonAsync<InviteResponse>();
            inviteCode = invite!.Code;
        }

        foreach (string name in SimulationWorld.MemberNames.Skip(1))
        {
            using HttpClient client = Factory.CreateMutatingClientAs(userIds[name]);
            using HttpResponseMessage accepted = await client.PostAsync($"/api/invites/{inviteCode}/accept", null);
            accepted.StatusCode.Should().Be(HttpStatusCode.OK, $"{name} accepts the shared invite code");
        }

        Dictionary<string, Guid> membershipIds = await Factory.QueryDbAsync(async db => await db.Memberships
            .Where(membership => membership.LeagueId == leagueId)
            .Select(membership => new { membership.Id, membership.User!.DisplayName })
            .ToDictionaryAsync(row => row.DisplayName, row => row.Id, StringComparer.Ordinal));

        Dictionary<string, SimMember> members = new(StringComparer.Ordinal);
        foreach (string name in SimulationWorld.MemberNames)
        {
            string endpoint = $"https://push.example/simulation/{name}";
            await Factory.ExecuteDbAsync(async db =>
            {
                db.PushSubscriptions.Add(new PushSubscription
                {
                    Id = Guid.CreateVersion7(),
                    UserId = userIds[name],
                    Endpoint = endpoint,
                    P256dh = "p256dh",
                    Auth = "auth",
                    CreatedUtc = _clock.GetUtcNow().UtcDateTime,
                });

                await db.SaveChangesAsync();
            });

            members[name] = new SimMember(name, userIds[name], membershipIds[name], endpoint);
        }

        Dictionary<long, Guid> gameIds = new();
        foreach (long cfbdGameId in RuleGames.Concat(ManualGames))
        {
            gameIds[cfbdGameId] = await FixtureGameData.GetGameIdAsync(Factory, cfbdGameId);
        }

        return new SimulationWorld(leagueId, members, gameIds);
    }

    private async Task AssertRosterAsync(SimulationWorld world)
    {
        using HttpClient michael = Factory.CreateClientAs(world["Michael"].UserId);
        MemberRow[]? rows = await michael.GetFromJsonAsync<MemberRow[]>($"/api/leagues/{world.LeagueId}/members");

        rows.Should().NotBeNull();
        rows!.Select(row => row.DisplayName).Should().BeEquivalentTo(SimulationWorld.MemberNames);
        rows.Where(row => row.Role == MembershipRole.Commissioner).Should().ContainSingle()
            .Which.DisplayName.Should().Be("Michael");
        rows.Should().OnlyContain(row => row.JoinedWeek == PriorWeek, "everybody joined during week 6");
        rows.Should().OnlyContain(row => !row.IsFormer);
    }

    /// <summary>
    /// Week 6, already played and scored, seeded directly: three bespoke <c>Games</c> rows of its
    /// own so that advancing the week 7 fixture snapshots can never move them, a locked set over
    /// them, everyone's picks, and the settled statuses the lock job would have written. The real
    /// <see cref="ScoringService"/> then closes it, which is what writes the
    /// <c>SeasonStandingsSnapshots</c> row for <c>ThroughWeek = 6</c>.
    /// </summary>
    private async Task SeedCompletedPriorWeekAsync(SimulationWorld world)
    {
        Guid setId = Guid.CreateVersion7();

        await Factory.ExecuteDbAsync(async db =>
        {
            Dictionary<int, Guid> teamIds = await db.Teams
                .Where(team => team.Classification == TeamClassification.Fbs)
                .ToDictionaryAsync(team => team.CfbdId, team => team.Id);

            DateTime kickoffUtc = new(2026, 10, 10, 16, 0, 0, DateTimeKind.Utc);

            (long CfbdGameId, int HomeCfbdId, int AwayCfbdId, int HomeScore, int AwayScore)[] games =
            [
                (PriorMichiganOhioState, 900101, 900105, 30, 20),
                (PriorGeorgiaAlabama, 900109, 900107, 24, 10),
                (PriorClemsonOregon, 900113, 900115, 17, 14),
            ];

            var set = new WeekGameSet
            {
                Id = setId,
                LeagueId = world.LeagueId,
                Week = PriorWeek,
                UsesOverride = false,
                GeneratedUtc = kickoffUtc.AddDays(-4),
                LockAtUtc = kickoffUtc,
                LockedUtc = kickoffUtc,
            };
            db.WeekGameSets.Add(set);

            var rowIds = new List<Guid>();
            foreach ((long cfbdGameId, int homeCfbdId, int awayCfbdId, int homeScore, int awayScore) in games)
            {
                var game = new Game
                {
                    Id = Guid.CreateVersion7(),
                    CfbdGameId = cfbdGameId,
                    SeasonYear = FixtureSeasonWeekSource.FixtureSeasonYear,
                    Week = PriorWeek,
                    HomeTeamId = teamIds[homeCfbdId],
                    AwayTeamId = teamIds[awayCfbdId],
                    KickoffUtc = kickoffUtc,
                    KickoffEasternDate = new DateOnly(2026, 10, 10),
                    IsSaturdayEastern = true,
                    IsConferenceGame = false,
                    Status = GameStatus.Final,
                    HomeScore = homeScore,
                    AwayScore = awayScore,
                };
                db.Games.Add(game);

                var row = new WeekGameSetGame
                {
                    Id = Guid.CreateVersion7(),
                    WeekGameSetId = set.Id,
                    GameId = game.Id,
                    Source = GameSetGameSource.Rule,
                    AddedUtc = set.GeneratedUtc,
                    ResolvedPointValue = StandardPointValue,
                };
                db.WeekGameSetGames.Add(row);
                rowIds.Add(row.Id);
            }

            // true = picked the home team (who won every one of the three).
            Dictionary<string, bool[]> priorPicks = new(StringComparer.Ordinal)
            {
                ["Michael"] = [true, true, true],
                ["Alyson"] = [true, false, false],
                ["Dance"] = [false, true, false],
                ["Alex"] = [false, false, false],
                ["Daniel"] = [true, true, false],
                ["Sam"] = [false, false, false],
            };

            List<Game> seededGames = [.. db.Games.Local.Where(game => game.Week == PriorWeek)];

            foreach ((string name, bool[] picks) in priorPicks)
            {
                Guid membershipId = world[name].MembershipId;

                for (int i = 0; i < rowIds.Count; i++)
                {
                    Game game = seededGames.Single(candidate => candidate.CfbdGameId == games[i].CfbdGameId);
                    db.Picks.Add(new Pick
                    {
                        Id = Guid.CreateVersion7(),
                        MembershipId = membershipId,
                        WeekGameSetGameId = rowIds[i],
                        PickedTeamId = picks[i] ? game.HomeTeamId : game.AwayTeamId,
                        UpdatedUtc = kickoffUtc.AddHours(-2),
                    });
                }

                db.WeekSubmissions.Add(new WeekSubmission
                {
                    MembershipId = membershipId,
                    WeekGameSetId = set.Id,
                    Status = SubmissionStatus.Locked,
                    SubmittedUtc = kickoffUtc.AddHours(-2),
                    LastChangedUtc = kickoffUtc,
                    HasUnseenGameChanges = false,
                });
            }

            await db.SaveChangesAsync();
        });

        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ScoringService>()
            .RescoreWeekAsync(setId, CancellationToken.None);
    }

    private async Task AssertPriorWeekAsync(SimulationWorld world)
    {
        using HttpClient michael = Factory.CreateClientAs(world["Michael"].UserId);
        WeekLeaderboard? week = await michael.GetFromJsonAsync<WeekLeaderboard>(
            $"/api/leagues/{world.LeagueId}/weeks/{PriorWeek}/leaderboard");

        week!.IsComplete.Should().BeTrue();
        week.Rows.Should().HaveCount(6);
        week.Rows.Single(row => row.DisplayName == "Michael").Points.Should().Be(30);
        week.Rows.Single(row => row.DisplayName == "Michael").IsWinner.Should().BeTrue();
        week.Rows.Single(row => row.DisplayName == "Daniel").Points.Should().Be(20);
        week.Rows.Single(row => row.DisplayName == "Sam").Points.Should().Be(0);

        int snapshotRows = await Factory.QueryDbAsync(db => db.SeasonStandingsSnapshots
            .CountAsync(row => row.LeagueId == world.LeagueId && row.ThroughWeek == PriorWeek));
        snapshotRows.Should().Be(6, "completing a week freezes one snapshot row per member");
    }

    // =========================================================================================
    // Picks
    // =========================================================================================

    /// <summary>
    /// Who picks what, by fixture game. <c>true</c> is the home team; a game missing from a
    /// member's map is a game they never picked.
    /// </summary>
    /// <remarks>
    /// Michigan / Texas and Maryland / Rutgers reproduce <c>WorkItems/Overview.txt</c> exactly:
    /// Michigan from Michael, Alyson, Alex and Daniel against Dance's Texas; Maryland from
    /// Michael, Alyson, Dance and Daniel against Alex's Rutgers.
    /// </remarks>
    private static Dictionary<string, Dictionary<long, bool>> PicksByMember() => new(StringComparer.Ordinal)
    {
        ["Michael"] = new()
        {
            [MichiganTexas] = true,
            [MarylandRutgers] = true,
            [AlabamaAuburn] = true,
            [GeorgiaKentucky] = true,
            [IowaStateKansas] = true,
            [OregonWashington] = true,
            [SanJoseHawaii] = true,
        },
        ["Alyson"] = new()
        {
            [MichiganTexas] = true,
            [MarylandRutgers] = true,
            [AlabamaAuburn] = true,
            [GeorgiaKentucky] = true,
            [IowaStateKansas] = false,
            [OregonWashington] = true,
            [SanJoseHawaii] = true,
        },
        ["Dance"] = new()
        {
            [MichiganTexas] = false,
            [MarylandRutgers] = true,
            [AlabamaAuburn] = false,
            [GeorgiaKentucky] = true,
            [IowaStateKansas] = false,
            [OregonWashington] = true,
            [SanJoseHawaii] = false,
        },
        ["Daniel"] = new()
        {
            [MichiganTexas] = true,
            [MarylandRutgers] = true,
            [AlabamaAuburn] = false,
            [GeorgiaKentucky] = false,
            [IowaStateKansas] = true,
            [OregonWashington] = false,
            [SanJoseHawaii] = true,
        },

        // Alex never finishes: Alabama / Auburn and Georgia / Kentucky stay unpicked, so the lock
        // job settles them Incomplete.
        ["Alex"] = new()
        {
            [MichiganTexas] = true,
            [MarylandRutgers] = false,
            [IowaStateKansas] = true,
            [OregonWashington] = false,
            [SanJoseHawaii] = true,
        },

        // Sam never opens the page at all.
        ["Sam"] = [],
    };

    private async Task MakeEveryonesPicksAsync(SimulationWorld world)
    {
        foreach ((string name, Dictionary<long, bool> picks) in PicksByMember())
        {
            if (picks.Count == 0)
            {
                continue;
            }

            using HttpClient client = Factory.CreateMutatingClientAs(world[name].UserId);

            foreach ((long cfbdGameId, bool pickHome) in picks)
            {
                GameSetGameDto game = world.GameDto(cfbdGameId);
                Guid teamId = pickHome ? game.HomeTeam.TeamId : game.AwayTeam.TeamId;

                using HttpResponseMessage response = await client.PutAsJsonAsync(
                    $"/api/leagues/{world.LeagueId}/weeks/{Week}/picks/me/{game.GameId}",
                    new SetPickRequest(teamId));
                response.StatusCode.Should().Be(HttpStatusCode.OK, $"{name} picks in {cfbdGameId}");
            }

            using HttpResponseMessage submitted = await client.PostAsync(
                $"/api/leagues/{world.LeagueId}/weeks/{Week}/picks/me/submit", null);

            if (picks.Count == 7)
            {
                submitted.StatusCode.Should().Be(HttpStatusCode.OK, $"{name} picked every game");
            }
            else
            {
                submitted.StatusCode.Should().Be(
                    HttpStatusCode.Conflict, $"{name} cannot submit with games left unpicked");
            }
        }
    }

    private async Task AssertPickRosterAsync(SimulationWorld world)
    {
        using HttpClient michael = Factory.CreateClientAs(world["Michael"].UserId);
        MemberStatusRow[]? roster = await michael.GetFromJsonAsync<MemberStatusRow[]>(
            $"/api/leagues/{world.LeagueId}/weeks/{Week}/picks/status");

        roster.Should().NotBeNull();
        roster!.Should().HaveCount(6);

        foreach (string name in new[] { "Michael", "Alyson", "Dance", "Daniel" })
        {
            roster.Single(row => row.DisplayName == name).Status.Should().Be(SubmissionStatus.Submitted);
        }

        MemberStatusRow alex = roster.Single(row => row.DisplayName == "Alex");
        alex.Status.Should().Be(SubmissionStatus.InProgress);
        alex.PickedCount.Should().Be(5);
        alex.TotalCount.Should().Be(7);

        MemberStatusRow sam = roster.Single(row => row.DisplayName == "Sam");
        sam.Status.Should().Be(SubmissionStatus.NotStarted);
        sam.PickedCount.Should().Be(0);
    }

    // =========================================================================================
    // Lock
    // =========================================================================================

    private async Task AssertLockedStatusesAsync(SimulationWorld world)
    {
        WeekGameSet lockedSet = await Factory.QueryDbAsync(db => db.WeekGameSets
            .AsNoTracking()
            .SingleAsync(set => set.LeagueId == world.LeagueId && set.Week == Week));

        lockedSet.LockedUtc.Should().Be(LockUtc.UtcDateTime, "the job stamps the instant it actually ran");

        Dictionary<Guid, SubmissionStatus> statuses = await Factory.QueryDbAsync(db => db.WeekSubmissions
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == lockedSet.Id)
            .ToDictionaryAsync(row => row.MembershipId, row => row.Status));

        statuses.Should().HaveCount(6);

        foreach (string name in new[] { "Michael", "Alyson", "Dance", "Daniel" })
        {
            statuses[world[name].MembershipId].Should().Be(SubmissionStatus.Locked, $"{name} submitted");
        }

        statuses[world["Alex"].MembershipId].Should().Be(
            SubmissionStatus.Incomplete, "Alex left two games unpicked");
        statuses[world["Sam"].MembershipId].Should().Be(
            SubmissionStatus.Incomplete, "Sam never started, and the job writes him a row anyway");

        // The frozen point value survives the lock's final re-resolution (Feature 03 section 3).
        int frozenTieValue = await Factory.QueryDbAsync(db => db.WeekGameSetGames
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == lockedSet.Id && row.GameId == world.GameIdOf(IowaStateKansas))
            .Select(row => row.ResolvedPointValue)
            .SingleAsync());
        frozenTieValue.Should().Be(TiePointValue);
    }

    private async Task AssertPicksAreFrozenAsync(SimulationWorld world)
    {
        GameSetGameDto game = world.GameDto(GeorgiaKentucky);

        using HttpClient alex = Factory.CreateMutatingClientAs(world["Alex"].UserId);
        using HttpResponseMessage refused = await alex.PutAsJsonAsync(
            $"/api/leagues/{world.LeagueId}/weeks/{Week}/picks/me/{game.GameId}",
            new SetPickRequest(game.HomeTeam.TeamId));

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "no mutation is accepted after the lock");

        using HttpClient alyson = Factory.CreateClientAs(world["Alyson"].UserId);
        WeekPicksResponse? everyone = await alyson.GetFromJsonAsync<WeekPicksResponse>(
            $"/api/leagues/{world.LeagueId}/weeks/{Week}/picks");

        everyone!.Members.Should().HaveCount(6, "every member the lock job settled is now visible");
    }

    /// <summary>
    /// The Overview worked example, read back off the real dashboard endpoint for the two games
    /// it documents. Sam, who never picked, is in <c>NoPick</c> on both and in nobody's
    /// <c>OppositePicks</c>, which is why the documented lists still match exactly.
    /// </summary>
    private async Task AssertOverviewDashboardsAsync(SimulationWorld world)
    {
        Guid michiganTexas = world.GameDto(MichiganTexas).GameId;
        Guid marylandRutgers = world.GameDto(MarylandRutgers).GameId;

        using (HttpClient dance = Factory.CreateClientAs(world["Dance"].UserId))
        {
            DashboardResponse? board = await dance.GetFromJsonAsync<DashboardResponse>(
                $"/api/leagues/{world.LeagueId}/weeks/{Week}/dashboard");

            board!.IsAvailable.Should().BeTrue();
            board.Games.Should().HaveCount(7);
            board.Games.Select(game => game.OppositeCount).Should().BeInDescendingOrder();

            DashboardGameDto game1 = board.Games.Single(game => game.Game.GameId == michiganTexas);
            game1.MyTeamId.Should().Be(world.GameDto(MichiganTexas).AwayTeam.TeamId, "Dance picked Texas");
            game1.MyOutcome.Should().Be(InfluenceOutcome.Pending);
            game1.OppositeCount.Should().Be(4);
            Names(game1.OppositePicks).Should().BeEquivalentTo(["Michael", "Alyson", "Alex", "Daniel"]);
            Names(game1.NoPick).Should().BeEquivalentTo(["Sam"]);

            DashboardGameDto game2 = board.Games.Single(game => game.Game.GameId == marylandRutgers);
            game2.MyTeamId.Should().Be(world.GameDto(MarylandRutgers).HomeTeam.TeamId, "Dance picked Maryland");
            game2.OppositeCount.Should().Be(1);
            Names(game2.OppositePicks).Should().BeEquivalentTo(["Alex"]);
            Names(game2.NoPick).Should().BeEquivalentTo(["Sam"]);
        }

        using (HttpClient alyson = Factory.CreateClientAs(world["Alyson"].UserId))
        {
            DashboardResponse? board = await alyson.GetFromJsonAsync<DashboardResponse>(
                $"/api/leagues/{world.LeagueId}/weeks/{Week}/dashboard");

            board!.IsAvailable.Should().BeTrue();

            DashboardGameDto game1 = board.Games.Single(game => game.Game.GameId == michiganTexas);
            game1.MyTeamId.Should().Be(world.GameDto(MichiganTexas).HomeTeam.TeamId, "Alyson picked Michigan");
            game1.OppositeCount.Should().Be(1);
            Names(game1.OppositePicks).Should().BeEquivalentTo(["Dance"]);
            Names(game1.NoPick).Should().BeEquivalentTo(["Sam"]);

            DashboardGameDto game2 = board.Games.Single(game => game.Game.GameId == marylandRutgers);
            game2.MyTeamId.Should().Be(world.GameDto(MarylandRutgers).HomeTeam.TeamId, "Alyson picked Maryland");
            game2.OppositeCount.Should().Be(1);
            Names(game2.OppositePicks).Should().BeEquivalentTo(["Alex"]);
            Names(game2.NoPick).Should().BeEquivalentTo(["Sam"]);
        }
    }

    // =========================================================================================
    // Game day
    // =========================================================================================

    private async Task WalkSnapshotsAsync(SimulationWorld world)
    {
        FixtureSnapshotState snapshots = Factory.Services.GetRequiredService<FixtureSnapshotState>();

        // Snapshots 1 and 2: nothing in this league's set has finished yet, so no GameWentFinal
        // reaches the scoring handler and the week has no result rows at all - not a row of zeroes.
        for (int snapshot = 1; snapshot <= 2; snapshot++)
        {
            await PollAsync(world, snapshots, snapshot);
            (await ResultCountAsync(world)).Should().Be(0, "scoring is driven by a game going final");
        }

        // Snapshot 3: Maryland and Georgia are final.
        await PollAsync(world, snapshots, 3);
        await AssertScoresAsync(world, expectedActiveGames: 7, ("Michael", 20, 2), ("Alyson", 20, 2),
            ("Dance", 20, 2), ("Daniel", 10, 1), ("Alex", 0, 0), ("Sam", 0, 0));

        // Snapshot 4: Michigan and Alabama are final, and Iowa State / Kansas ends 24-24.
        await PollAsync(world, snapshots, 4);
        await AssertScoresAsync(world, expectedActiveGames: 7, ("Michael", 40, 4), ("Alyson", 40, 4),
            ("Dance", 20, 2), ("Daniel", 20, 2), ("Alex", 10, 1), ("Sam", 0, 0));
        await AssertTieNeedsReviewAsync(world);
        (await IsCompleteAsync(world)).Should().BeFalse("a Final tie has no winner and holds the week open");

        // Snapshot 5: the 22:00 ET Oregon game is final.
        await PollAsync(world, snapshots, 5);
        await AssertScoresAsync(world, expectedActiveGames: 7, ("Michael", 50, 5), ("Alyson", 50, 5),
            ("Dance", 30, 3), ("Daniel", 20, 2), ("Alex", 10, 1), ("Sam", 0, 0));

        // Snapshot 6: the 22:30 ET game finishes at 01:45 ET on Sunday and still counts for week 7.
        await PollAsync(world, snapshots, 6);
        await AssertScoresAsync(world, expectedActiveGames: 7, ("Michael", 60, 6), ("Alyson", 60, 6),
            ("Dance", 30, 3), ("Daniel", 30, 3), ("Alex", 20, 2), ("Sam", 0, 0));

        (await IsCompleteAsync(world)).Should().BeFalse("every game has finished, but the tie is still unsettled");
    }

    private async Task PollAsync(SimulationWorld world, FixtureSnapshotState snapshots, int snapshot)
    {
        _clock.Set(SnapshotInstantsUtc[snapshot - 1]);
        snapshots.Set(snapshot);

        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        await SaturdayPoller.PollOnceAsync(
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            FixtureSaturday,
            scope.ServiceProvider.GetRequiredService<ILogger<SaturdayPoller>>(),
            CancellationToken.None);

        _ = world;
    }

    private async Task AssertTieNeedsReviewAsync(SimulationWorld world)
    {
        using HttpClient michael = Factory.CreateClientAs(world["Michael"].UserId);
        DataStatusResponse? status = await michael.GetFromJsonAsync<DataStatusResponse>("/api/admin/data-status");

        NeedsReviewGameDto row = status!.NeedsReview.Should()
            .ContainSingle(candidate => candidate.LeagueId == world.LeagueId).Subject;

        row.Reason.Should().Be("Tie");
        row.GameId.Should().Be(world.GameIdOf(IowaStateKansas));
        row.GameSetGameId.Should().NotBeNull("P5-05 drives a one-tap void straight off this row");
    }

    // =========================================================================================
    // Corrections
    // =========================================================================================

    private async Task VoidAndOverrideAsync(SimulationWorld world)
    {
        using HttpClient michael = Factory.CreateMutatingClientAs(world["Michael"].UserId);

        // A void: Alabama / Auburn is struck from the week, so its 10 points score for nobody.
        using (HttpResponseMessage voided = await michael.PostAsJsonAsync(
            $"/api/leagues/{world.LeagueId}/weeks/{Week}/gameset/games/{world.GameIdOf(AlabamaAuburn)}/void",
            new VoidGameRequest("Abandoned at half time; simulation void.")))
        {
            voided.StatusCode.Should().Be(HttpStatusCode.OK);
            GameSetGameDto? dto = await voided.Content.ReadFromJsonAsync<GameSetGameDto>();
            dto!.IsVoided.Should().BeTrue();
        }

        await AssertScoresAsync(world, expectedActiveGames: 6, ("Michael", 50, 5), ("Alyson", 50, 5),
            ("Dance", 30, 3), ("Daniel", 30, 3), ("Alex", 20, 2), ("Sam", 0, 0));
        (await IsCompleteAsync(world)).Should().BeFalse("the tie is still unsettled");

        // An override: the commissioner awards the tie to Kansas, the away side.
        using (HttpResponseMessage overridden = await michael.PostAsJsonAsync(
            $"/api/leagues/{world.LeagueId}/weeks/{Week}/gameset/games/{world.GameIdOf(IowaStateKansas)}/override-result",
            new OverrideResultRequest(world.GameDto(IowaStateKansas).AwayTeam.TeamId, "Kansas won the overtime.")))
        {
            overridden.StatusCode.Should().Be(HttpStatusCode.OK);
            GameSetGameDto? dto = await overridden.Content.ReadFromJsonAsync<GameSetGameDto>();
            dto!.WinnerTeamId.Should().Be(world.GameDto(IowaStateKansas).AwayTeam.TeamId);
        }
    }

    // =========================================================================================
    // The closed week
    // =========================================================================================

    private async Task AssertCompletedWeekAsync(SimulationWorld world)
    {
        await AssertScoresAsync(world, expectedActiveGames: 6, ("Michael", 50, 5), ("Alyson", 65, 6),
            ("Dance", 45, 4), ("Daniel", 30, 3), ("Alex", 20, 2), ("Sam", 0, 0));

        (await IsCompleteAsync(world)).Should().BeTrue("every active game now has a winner");

        using HttpClient michael = Factory.CreateClientAs(world["Michael"].UserId);
        WeekLeaderboard? week = await michael.GetFromJsonAsync<WeekLeaderboard>(
            $"/api/leagues/{world.LeagueId}/weeks/{Week}/leaderboard");

        week!.IsComplete.Should().BeTrue();
        week.Rows.Select(row => row.DisplayName).Should().ContainInOrder("Alyson", "Michael", "Dance", "Daniel", "Alex", "Sam");
        week.Rows.Select(row => row.Rank).Should().Equal(1, 2, 3, 4, 5, 6);
        week.Rows.Single(row => row.DisplayName == "Alyson").IsWinner.Should().BeTrue();
        week.Rows.Single(row => row.DisplayName == "Alyson").Correct.Should().Be(6);
        week.Rows.Should().OnlyContain(row => row.Total == 6, "the voided game is not an active game any more");
    }

    private async Task AssertSeasonLeaderboardAsync(SimulationWorld world)
    {
        using HttpClient alyson = Factory.CreateClientAs(world["Alyson"].UserId);
        SeasonLeaderboard? season = await alyson.GetFromJsonAsync<SeasonLeaderboard>(
            $"/api/leagues/{world.LeagueId}/leaderboard");

        season!.ThroughWeek.Should().Be(Week);
        season.Rows.Select(row => (row.Rank, row.DisplayName, row.TotalPoints)).Should().Equal(
        [
            (1, "Michael", 80),
            (2, "Alyson", 75),
            (3, "Dance", 55),
            (4, "Daniel", 50),
            (5, "Alex", 20),
            (6, "Sam", 0),
        ]);

        season.Rows.Single(row => row.DisplayName == "Michael").WeeklyWins.Should().Be(1, "week 6");
        season.Rows.Single(row => row.DisplayName == "Alyson").WeeklyWins.Should().Be(1, "week 7");
        season.Rows.Where(row => row.DisplayName is "Dance" or "Daniel" or "Alex" or "Sam")
            .Should().OnlyContain(row => row.WeeklyWins == 0);

        season.Rows.Single(row => row.DisplayName == "Michael").PointsBehind.Should().Be(0);
        season.Rows.Single(row => row.DisplayName == "Alyson").PointsBehind.Should().Be(5);
        season.Rows.Single(row => row.IsMe).DisplayName.Should().Be("Alyson");
    }

    private async Task AssertGridAsync(SimulationWorld world)
    {
        using HttpClient daniel = Factory.CreateClientAs(world["Daniel"].UserId);
        WeekGrid? grid = await daniel.GetFromJsonAsync<WeekGrid>(
            $"/api/leagues/{world.LeagueId}/weeks/{Week}/grid");

        grid!.Games.Should().HaveCount(7, "a voided game is still shown in the grid");
        grid.Members.Select(member => member.DisplayName)
            .Should().Equal("Alex", "Alyson", "Dance", "Daniel", "Michael", "Sam");
        grid.Cells.Should().HaveCount(7 * 6);

        Guid Cell(long cfbdGameId) => grid.Games.Single(game => game.GameId == world.GameIdOf(cfbdGameId)).GameSetGameId!.Value;

        GridCell Find(long cfbdGameId, string name) => grid.Cells.Single(cell =>
            cell.GameSetGameId == Cell(cfbdGameId) && cell.MembershipId == world[name].MembershipId);

        Find(AlabamaAuburn, "Michael").Outcome.Should().Be(GridOutcome.Voided);
        Find(AlabamaAuburn, "Sam").Outcome.Should().Be(GridOutcome.Voided);

        Find(IowaStateKansas, "Alyson").Outcome.Should().Be(GridOutcome.Correct, "the override made Kansas the winner");
        Find(IowaStateKansas, "Michael").Outcome.Should().Be(GridOutcome.Incorrect);

        Find(GeorgiaKentucky, "Alex").Outcome.Should().Be(GridOutcome.NoPick);
        Find(SanJoseHawaii, "Dance").Outcome.Should().Be(GridOutcome.Incorrect);
        Find(MichiganTexas, "Daniel").Outcome.Should().Be(GridOutcome.Correct);

        grid.Cells.Where(cell => cell.MembershipId == world["Sam"].MembershipId)
            .Should().OnlyContain(cell => cell.TeamId == null);
    }

    private async Task AssertSnapshotsAndTrendsAsync(SimulationWorld world)
    {
        int snapshotRows = await Factory.QueryDbAsync(db => db.SeasonStandingsSnapshots
            .CountAsync(row => row.LeagueId == world.LeagueId));
        snapshotRows.Should().Be(12, "two complete weeks, six members each");

        Dictionary<Guid, int> weekSevenRanks = await Factory.QueryDbAsync(db => db.SeasonStandingsSnapshots
            .AsNoTracking()
            .Where(row => row.LeagueId == world.LeagueId && row.ThroughWeek == Week)
            .ToDictionaryAsync(row => row.MembershipId, row => row.Rank));

        weekSevenRanks[world["Michael"].MembershipId].Should().Be(1);
        weekSevenRanks[world["Alyson"].MembershipId].Should().Be(2);

        using HttpClient michael = Factory.CreateClientAs(world["Michael"].UserId);
        SeasonLeaderboard? season = await michael.GetFromJsonAsync<SeasonLeaderboard>(
            $"/api/leagues/{world.LeagueId}/leaderboard");

        StandingsTrend TrendOf(string name) => season!.Rows.Single(row => row.DisplayName == name).Trend;

        TrendOf("Michael").Should().Be(StandingsTrend.Same, "first through week 6 and week 7 alike");
        TrendOf("Alyson").Should().Be(StandingsTrend.Up, "third through week 6, second through week 7");
        TrendOf("Dance").Should().Be(StandingsTrend.Same, "third both times");
        TrendOf("Daniel").Should().Be(StandingsTrend.Down, "second through week 6, fourth through week 7");
        TrendOf("Alex").Should().Be(StandingsTrend.Same);
        TrendOf("Sam").Should().Be(StandingsTrend.Down, "joint fifth through week 6, last through week 7");
    }

    private async Task AssertAuditTrailAsync(SimulationWorld world)
    {
        using HttpClient dance = Factory.CreateClientAs(world["Dance"].UserId);
        AuditEntry[]? audit = await dance.GetFromJsonAsync<AuditEntry[]>($"/api/leagues/{world.LeagueId}/audit");

        audit.Should().NotBeNull();
        audit!.Should().Contain(entry => entry.Action == nameof(AuditAction.GameVoided));
        audit.Should().Contain(entry => entry.Action == nameof(AuditAction.ResultOverride));
        audit.Should().Contain(entry => entry.Action == nameof(AuditAction.GameManuallyAdded));
        audit.Should().OnlyContain(entry => entry.ActorName == "Michael");
    }

    // =========================================================================================
    // Plumbing
    // =========================================================================================

    /// <summary>Moves the clock to <paramref name="instant"/> and runs one real scheduler pass.</summary>
    private async Task TickAsync(DateTimeOffset instant)
    {
        _clock.Set(instant);
        await Factory.Services.GetRequiredService<SchedulerTick>().TickAsync(instant, CancellationToken.None);
    }

    private async Task<WeekGameSetResponse> GetWeekGameSetAsync(SimulationWorld world)
    {
        using HttpClient michael = Factory.CreateClientAs(world["Michael"].UserId);
        WeekGameSetResponse? response = await michael.GetFromJsonAsync<WeekGameSetResponse>(
            $"/api/leagues/{world.LeagueId}/weeks/{Week}/gameset");

        response.Should().NotBeNull();
        return response!;
    }

    private Task<int> ResultCountAsync(SimulationWorld world) =>
        Factory.QueryDbAsync(db => db.WeekResults
            .AsNoTracking()
            .CountAsync(row => row.WeekGameSet!.LeagueId == world.LeagueId && row.WeekGameSet.Week == Week));

    private Task<bool> IsCompleteAsync(SimulationWorld world) =>
        Factory.QueryDbAsync(db => db.WeekGameSets
            .AsNoTracking()
            .Where(set => set.LeagueId == world.LeagueId && set.Week == Week)
            .Select(set => set.IsComplete)
            .SingleAsync());

    private async Task AssertScoresAsync(
        SimulationWorld world,
        int expectedActiveGames,
        params (string Name, int Points, int Correct)[] expected)
    {
        Guid setId = await Factory.QueryDbAsync(db => db.WeekGameSets
            .AsNoTracking()
            .Where(set => set.LeagueId == world.LeagueId && set.Week == Week)
            .Select(set => set.Id)
            .SingleAsync());

        Dictionary<Guid, (int Points, int Correct, int Active)> results = await Factory.QueryDbAsync(db =>
            db.WeekResults
                .AsNoTracking()
                .Where(row => row.WeekGameSetId == setId)
                .ToDictionaryAsync(
                    row => row.MembershipId,
                    row => ValueTuple.Create(row.Points, row.CorrectCount, row.ActiveGameCount)));

        results.Should().HaveCount(expected.Length);

        foreach ((string name, int points, int correct) in expected)
        {
            (int actualPoints, int actualCorrect, int actualActive) = results[world[name].MembershipId];

            actualPoints.Should().Be(points, $"{name}'s points");
            actualCorrect.Should().Be(correct, $"{name}'s correct count");
            actualActive.Should().Be(expectedActiveGames, $"{name}'s active game count");
        }
    }

    private static string[] Names(IEnumerable<MemberRef> members) =>
        [.. members.Select(member => member.DisplayName)];

    /// <summary>One person in the simulation.</summary>
    /// <param name="Name">Their display name, which is also the key everywhere in this test.</param>
    /// <param name="UserId">Their user, for <c>CreateClientAs</c>.</param>
    /// <param name="MembershipId">Their membership in the simulated league.</param>
    /// <param name="PushEndpoint">Their one subscribed device.</param>
    private sealed record SimMember(string Name, Guid UserId, Guid MembershipId, string PushEndpoint);

    /// <summary>The league, its people, and the fixture games it plays over.</summary>
    private sealed class SimulationWorld
    {
        /// <summary>
        /// The Overview's five names, commissioner first, plus Sam - the sixth member who never
        /// opens the picks page (see the class remarks).
        /// </summary>
        public static readonly string[] MemberNames = ["Michael", "Alyson", "Dance", "Alex", "Daniel", "Sam"];

        private readonly Dictionary<string, SimMember> _members;
        private readonly Dictionary<long, Guid> _gameIdsByCfbdId;
        private readonly Dictionary<Guid, long> _cfbdIdsByGameId;
        private Dictionary<long, GameSetGameDto> _gamesByCfbdId = [];

        public SimulationWorld(
            Guid leagueId,
            Dictionary<string, SimMember> members,
            Dictionary<long, Guid> gameIdsByCfbdId)
        {
            LeagueId = leagueId;
            _members = members;
            _gameIdsByCfbdId = gameIdsByCfbdId;
            _cfbdIdsByGameId = gameIdsByCfbdId.ToDictionary(pair => pair.Value, pair => pair.Key);
        }

        public Guid LeagueId { get; }

        public SimMember this[string name] => _members[name];

        public Guid GameIdOf(long cfbdGameId) => _gameIdsByCfbdId[cfbdGameId];

        public long CfbdIdOf(Guid gameId) => _cfbdIdsByGameId[gameId];

        public GameSetGameDto GameDto(long cfbdGameId) => _gamesByCfbdId[cfbdGameId];

        /// <summary>Keeps the generated set's DTOs so picks can name a team without re-reading.</summary>
        public void RememberGames(IEnumerable<GameSetGameDto> games) =>
            _gamesByCfbdId = games.ToDictionary(game => CfbdIdOf(game.GameId), game => game);
    }
}
