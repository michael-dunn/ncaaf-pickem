using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// The Overview worked example (<c>tests/NcaafPickEm.Fixtures/Data/influence-example.json</c>),
/// built for real through the HTTP endpoints: a league with Michael as commissioner and Alyson,
/// Dance, Alex and Daniel as members, holding exactly the fixture's two games (Michigan/Texas
/// 700001, Maryland/Rutgers 700002 - manually added, not generated, so no other ranked game from
/// the Top 25 rule sneaks in) with the fixture's own picks already made.
/// </summary>
public sealed record DashboardWeekScenario(
    Guid LeagueId,
    Guid CommissionerUserId,
    Guid WeekGameSetId,
    GameSetGameDto MichiganTexas,
    GameSetGameDto MarylandRutgers,
    IReadOnlyDictionary<string, Guid> UserIdByName,
    IReadOnlyDictionary<string, Guid> MembershipIdByName)
{
    /// <summary>The week every scenario is built for: the fixture week.</summary>
    public const int Week = FixtureGameData.Week;

    /// <summary>Route for this league's fixture-week dashboard.</summary>
    public string DashboardRoute => $"/api/leagues/{LeagueId}/weeks/{Week}/dashboard";

    /// <summary>
    /// Seeds the fixture reference data, the league and its five members, adds the two fixture
    /// games, and records every member's pick exactly as <c>influence-example.json</c> documents.
    /// Does not lock the week - call <see cref="LockAsync"/> afterwards.
    /// </summary>
    /// <param name="factory">
    /// The app to seed through. Pass a factory whose clock is inside the fixture week
    /// (<see cref="ApiTestFixture.PinnedFactory"/>), or the current-week guard refuses every pick.
    /// </param>
    public static async Task<DashboardWeekScenario> CreateAsync(ApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await FixtureGameData.EnsureSeededAsync(factory);

        // The two fixture games are rows in the shared Games table (common to every league in
        // the run's one database). Another test class (LiveScoreApplyTests) drives them to Final
        // through the same fixture week and does not restore them, so pin them back to Scheduled
        // with no score here rather than assuming whatever state a previous test left behind.
        await ResetGameAsync(factory, 700001);
        await ResetGameAsync(factory, 700002);

        User michael = await factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, $"Michael {Suffix()}"));
        User alyson = await factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, $"Alyson {Suffix()}"));
        User dance = await factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, $"Dance {Suffix()}"));
        User alex = await factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, $"Alex {Suffix()}"));
        User daniel = await factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, $"Daniel {Suffix()}"));

        League league = await factory.QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, michael));
        Membership michaelMembership = await factory.QueryDbAsync(db =>
            TestUsers.CreateMembershipAsync(db, league, michael, MembershipRole.Commissioner));
        Membership alysonMembership = await factory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, alyson));
        Membership danceMembership = await factory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, dance));
        Membership alexMembership = await factory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, alex));
        Membership danielMembership = await factory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, daniel));

        Guid michiganTexasGameId = await FixtureGameData.GetGameIdAsync(factory, 700001);
        Guid marylandRutgersGameId = await FixtureGameData.GetGameIdAsync(factory, 700002);

        using HttpClient commish = factory.CreateMutatingClientAs(michael.Id);

        GameSetGameDto michiganTexas = await AddGameAsync(commish, league.Id, michiganTexasGameId);
        GameSetGameDto marylandRutgers = await AddGameAsync(commish, league.Id, marylandRutgersGameId);

        using HttpClient alysonClient = factory.CreateMutatingClientAs(alyson.Id);
        using HttpClient danceClient = factory.CreateMutatingClientAs(dance.Id);
        using HttpClient alexClient = factory.CreateMutatingClientAs(alex.Id);
        using HttpClient danielClient = factory.CreateMutatingClientAs(daniel.Id);
        using HttpClient michaelClient = commish;

        // Michigan vs Texas: Michael, Alyson, Alex, Daniel pick Michigan (home); Dance picks Texas (away).
        await PickAsync(michaelClient, league.Id, michiganTexas, michiganTexas.HomeTeam.TeamId);
        await PickAsync(alysonClient, league.Id, michiganTexas, michiganTexas.HomeTeam.TeamId);
        await PickAsync(alexClient, league.Id, michiganTexas, michiganTexas.HomeTeam.TeamId);
        await PickAsync(danielClient, league.Id, michiganTexas, michiganTexas.HomeTeam.TeamId);
        await PickAsync(danceClient, league.Id, michiganTexas, michiganTexas.AwayTeam.TeamId);

        // Maryland vs Rutgers: Michael, Alyson, Dance, Daniel pick Maryland (home); Alex picks Rutgers (away).
        await PickAsync(michaelClient, league.Id, marylandRutgers, marylandRutgers.HomeTeam.TeamId);
        await PickAsync(alysonClient, league.Id, marylandRutgers, marylandRutgers.HomeTeam.TeamId);
        await PickAsync(danceClient, league.Id, marylandRutgers, marylandRutgers.HomeTeam.TeamId);
        await PickAsync(danielClient, league.Id, marylandRutgers, marylandRutgers.HomeTeam.TeamId);
        await PickAsync(alexClient, league.Id, marylandRutgers, marylandRutgers.AwayTeam.TeamId);

        Guid setId = await factory.QueryDbAsync(db => db.WeekGameSets
            .Where(set => set.LeagueId == league.Id && set.Week == Week)
            .Select(set => set.Id)
            .FirstAsync());

        Dictionary<string, Guid> userIdByName = new(StringComparer.Ordinal)
        {
            ["Michael"] = michael.Id,
            ["Alyson"] = alyson.Id,
            ["Dance"] = dance.Id,
            ["Alex"] = alex.Id,
            ["Daniel"] = daniel.Id,
        };

        Dictionary<string, Guid> membershipIdByName = new(StringComparer.Ordinal)
        {
            ["Michael"] = michaelMembership.Id,
            ["Alyson"] = alysonMembership.Id,
            ["Dance"] = danceMembership.Id,
            ["Alex"] = alexMembership.Id,
            ["Daniel"] = danielMembership.Id,
        };

        return new DashboardWeekScenario(
            league.Id, michael.Id, setId, michiganTexas, marylandRutgers, userIdByName, membershipIdByName);
    }

    /// <summary>
    /// Locks the week by actually running P4-02's <see cref="LockWeekJob"/> (not by hand-setting
    /// <c>LockedUtc</c>), so the dashboard sees the same frozen point values and
    /// <c>WeekSubmissions</c> rows the real job writes.
    /// </summary>
    /// <param name="factory">The app whose database to lock through.</param>
    /// <param name="dueUtc">The occurrence's due instant, for the job's own log line only -
    /// <see cref="LockWeekJob"/> reads the set's persisted <c>LockAtUtc</c>, not this value, to
    /// decide who joined before lock.</param>
    public async Task LockAsync(ApiFactory factory, DateTime dueUtc)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using AsyncServiceScope scope = factory.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        LockWeekJob job = ActivatorUtilities.CreateInstance<LockWeekJob>(scope.ServiceProvider);
        await job.RunAsync(
            new OneShotOccurrence(WeekGameSetId.ToString("N"), new DateTimeOffset(dueUtc, TimeSpan.Zero)),
            CancellationToken.None);
    }

    private static async Task ResetGameAsync(ApiFactory factory, long cfbdGameId) =>
        await factory.ExecuteDbAsync(async db =>
        {
            Game game = await db.Games.SingleAsync(candidate => candidate.CfbdGameId == cfbdGameId);
            game.Status = GameStatus.Scheduled;
            game.HomeScore = null;
            game.AwayScore = null;
            game.Period = null;
            game.Clock = null;
            await db.SaveChangesAsync();
        });

    private static async Task<GameSetGameDto> AddGameAsync(HttpClient commish, Guid leagueId, Guid gameId)
    {
        using HttpResponseMessage response = await commish.PostAsJsonAsync(
            $"/api/leagues/{leagueId}/weeks/{Week}/gameset/games", new AddGameRequest(gameId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WeekGameSetResponse? body = await response.Content.ReadFromJsonAsync<WeekGameSetResponse>();
        return body!.Games.Single(g => g.GameId == gameId);
    }

    private static async Task PickAsync(HttpClient client, Guid leagueId, GameSetGameDto game, Guid teamId)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{leagueId}/weeks/{Week}/picks/me/{game.GameId}", new SetPickRequest(teamId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string Suffix() => Guid.CreateVersion7().ToString()[..8];
}
