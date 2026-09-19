using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Manual add/remove of a game from a week's set (Feature 02, P3-03):
/// <c>POST/DELETE /api/leagues/{leagueId}/weeks/{week}/gameset/games[/{gameId}]</c>.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class ManualAddRemoveTests
{
    private readonly ApiTestFixture _fixture;

    public ManualAddRemoveTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(League League, HttpClient Client, Guid CommissionerMembershipId)> CreateCommishLeagueAsync()
    {
        await FixtureGameData.EnsureSeededAsync(_fixture.Factory);

        User commissioner = await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "Commish"));
        League league = await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, commissioner));
        Membership membership = await _fixture.Factory.QueryDbAsync(
            db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));

        HttpClient client = _fixture.Factory.CreateMutatingClientAs(commissioner.Id);
        return (league, client, membership.Id);
    }

    [Fact]
    public async Task GivenASaturdayFbsGame_WhenAddingManually_ThenItIsAddedAndAudited()
    {
        (League league, HttpClient client, Guid membershipId) = await CreateCommishLeagueAsync();
        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700005); // Ohio State / Wisconsin, Saturday FBS

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games", new AddGameRequest(gameId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WeekGameSetResponse? body = await response.Content.ReadFromJsonAsync<WeekGameSetResponse>();
        body!.Games.Should().ContainSingle(g => g.GameId == gameId && g.Source == GameSetGameSource.Manual);

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            AuditLogEntry entry = await db.AuditLog.SingleAsync(e => e.LeagueId == league.Id && e.Action == AuditAction.GameManuallyAdded);
            entry.ActorMembershipId.Should().Be(membershipId);
        });
    }

    [Fact]
    public async Task GivenAnFcsInvolvedGame_WhenAddingManually_ThenItIs409()
    {
        (League league, HttpClient client, _) = await CreateCommishLeagueAsync();
        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700013); // Penn State / Youngstown State (FCS)

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games", new AddGameRequest(gameId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenAFridayGame_WhenAddingManually_ThenItIs409()
    {
        (League league, HttpClient client, _) = await CreateCommishLeagueAsync();
        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700015); // Boise State / Fresno State, Friday

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games", new AddGameRequest(gameId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenALockedWeek_WhenAddingManually_ThenItIs409()
    {
        (League league, HttpClient client, _) = await CreateCommishLeagueAsync();
        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700005);

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            db.WeekGameSets.Add(new WeekGameSet
            {
                Id = Guid.CreateVersion7(),
                LeagueId = league.Id,
                Week = 7,
                UsesOverride = false,
                GeneratedUtc = DateTime.UtcNow,
                LockedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games", new AddGameRequest(gameId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenAWeekAlreadyAtFiftyGames_WhenAddingA51st_ThenItIs409()
    {
        // A synthetic season/week of its own (2098/week 1), never touched by another test, so the
        // 51 extra games inserted here cannot contaminate any other test's eligible-game pool for
        // the shared Week 7, 2026 fixture schedule.
        await FixtureGameData.EnsureSeededAsync(_fixture.Factory);

        const int seasonYear = 2098;
        const int week = 1;

        User commissioner = await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "Commish"));
        League league = await _fixture.Factory.QueryDbAsync(async db =>
        {
            var created = new League
            {
                Id = Guid.CreateVersion7(),
                Name = $"Overflow League {Guid.CreateVersion7().ToString()[..8]}",
                SeasonYear = seasonYear,
                FirstWeek = week,
                LastWeek = week,
                DefaultPointValue = 10,
                CreatedByUserId = commissioner.Id,
                CreatedUtc = DateTime.UtcNow,
            };
            db.Leagues.Add(created);
            await db.SaveChangesAsync();
            return created;
        });
        await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));
        HttpClient client = _fixture.Factory.CreateMutatingClientAs(commissioner.Id);

        WeekGameSet set = await _fixture.Factory.QueryDbAsync(async db =>
        {
            var created = new WeekGameSet
            {
                Id = Guid.CreateVersion7(),
                LeagueId = league.Id,
                Week = week,
                UsesOverride = false,
                GeneratedUtc = DateTime.UtcNow,
            };
            db.WeekGameSets.Add(created);
            await db.SaveChangesAsync();
            return created;
        });

        Guid michiganId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900101);
        Guid otherTeamId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900102);
        DateTime kickoffUtc = new(2098, 1, 4, 17, 0, 0, DateTimeKind.Utc); // 2098-01-04 is a Saturday.

        Guid fiftyFirstGameId = Guid.CreateVersion7();

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            for (int i = 0; i < 51; i++)
            {
                var game = new Domain.Seasons.Game
                {
                    Id = i == 50 ? fiftyFirstGameId : Guid.CreateVersion7(),
                    CfbdGameId = 92_000_000 + i,
                    SeasonYear = seasonYear,
                    Week = week,
                    HomeTeamId = michiganId,
                    AwayTeamId = otherTeamId,
                    KickoffUtc = kickoffUtc,
                    KickoffEasternDate = DateOnly.FromDateTime(kickoffUtc),
                    IsSaturdayEastern = true,
                    IsConferenceGame = false,
                    Status = GameStatus.Scheduled,
                };
                db.Games.Add(game);

                if (i < 50)
                {
                    db.WeekGameSetGames.Add(new WeekGameSetGame
                    {
                        Id = Guid.CreateVersion7(),
                        WeekGameSetId = set.Id,
                        GameId = game.Id,
                        Source = GameSetGameSource.Manual,
                        IsRemoved = false,
                    });
                }
            }

            await db.SaveChangesAsync();
        });

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/{week}/gameset/games", new AddGameRequest(fiftyFirstGameId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenAWeekAlreadyAtFiftyGames_WhenReAddingAPreviouslyRemovedGame_ThenItIs409()
    {
        // Same shape as the 51st-game test, on a season year of its own (2097) so the synthetic
        // games cannot leak into another test's eligible pool. The 51st game already has a row,
        // flagged removed: flipping it back would take the set to 51 active games, so the cap
        // must refuse it exactly as it refuses a brand new row.
        await FixtureGameData.EnsureSeededAsync(_fixture.Factory);

        const int seasonYear = 2097;
        const int week = 1;

        User commissioner = await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "Commish"));
        League league = await _fixture.Factory.QueryDbAsync(async db =>
        {
            var created = new League
            {
                Id = Guid.CreateVersion7(),
                Name = $"Re-add League {Guid.CreateVersion7().ToString()[..8]}",
                SeasonYear = seasonYear,
                FirstWeek = week,
                LastWeek = week,
                DefaultPointValue = 10,
                CreatedByUserId = commissioner.Id,
                CreatedUtc = DateTime.UtcNow,
            };
            db.Leagues.Add(created);
            await db.SaveChangesAsync();
            return created;
        });
        await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));
        HttpClient client = _fixture.Factory.CreateMutatingClientAs(commissioner.Id);

        WeekGameSet set = await _fixture.Factory.QueryDbAsync(async db =>
        {
            var created = new WeekGameSet
            {
                Id = Guid.CreateVersion7(),
                LeagueId = league.Id,
                Week = week,
                UsesOverride = false,
                GeneratedUtc = DateTime.UtcNow,
            };
            db.WeekGameSets.Add(created);
            await db.SaveChangesAsync();
            return created;
        });

        Guid michiganId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900101);
        Guid otherTeamId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900102);
        DateTime kickoffUtc = new(2097, 1, 5, 17, 0, 0, DateTimeKind.Utc); // 2097-01-05 is a Saturday.

        Guid removedGameId = Guid.CreateVersion7();

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            for (int i = 0; i < 51; i++)
            {
                var game = new Domain.Seasons.Game
                {
                    Id = i == 50 ? removedGameId : Guid.CreateVersion7(),
                    CfbdGameId = 93_000_000 + i,
                    SeasonYear = seasonYear,
                    Week = week,
                    HomeTeamId = michiganId,
                    AwayTeamId = otherTeamId,
                    KickoffUtc = kickoffUtc,
                    KickoffEasternDate = DateOnly.FromDateTime(kickoffUtc),
                    IsSaturdayEastern = true,
                    IsConferenceGame = false,
                    Status = GameStatus.Scheduled,
                };
                db.Games.Add(game);

                db.WeekGameSetGames.Add(new WeekGameSetGame
                {
                    Id = Guid.CreateVersion7(),
                    WeekGameSetId = set.Id,
                    GameId = game.Id,
                    Source = GameSetGameSource.Manual,
                    IsRemoved = i == 50,
                    RemovedReason = i == 50 ? "Manual" : null,
                });
            }

            await db.SaveChangesAsync();
        });

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/{week}/gameset/games", new AddGameRequest(removedGameId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenAnActiveGame_WhenRemovingManually_ThenItIsFlaggedAndAnExistingPickSurvives()
    {
        (League league, HttpClient client, Guid membershipId) = await CreateCommishLeagueAsync();
        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700005);
        Guid michiganId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900101);

        await client.PostAsJsonAsync($"/api/leagues/{league.Id}/weeks/7/gameset/games", new AddGameRequest(gameId));

        Guid gameSetGameId = await _fixture.Factory.QueryDbAsync(
            db => db.WeekGameSetGames.Where(r => r.GameId == gameId && r.WeekGameSet!.LeagueId == league.Id).Select(r => r.Id).SingleAsync());

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            db.Picks.Add(new Pick
            {
                Id = Guid.CreateVersion7(),
                MembershipId = membershipId,
                WeekGameSetGameId = gameSetGameId,
                PickedTeamId = michiganId,
                UpdatedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        using HttpResponseMessage response = await client.DeleteAsync($"/api/leagues/{league.Id}/weeks/7/gameset/games/{gameId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WeekGameSetResponse? body = await response.Content.ReadFromJsonAsync<WeekGameSetResponse>();
        body!.Games.Should().NotContain(g => g.GameId == gameId);

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            WeekGameSetGame row = await db.WeekGameSetGames.SingleAsync(r => r.Id == gameSetGameId);
            row.IsRemoved.Should().BeTrue();
            row.RemovedReason.Should().Be("Manual");

            Pick pick = await db.Picks.SingleAsync(p => p.WeekGameSetGameId == gameSetGameId);
            pick.PickedTeamId.Should().Be(michiganId);
        });
    }

    [Fact]
    public async Task GivenTheFixtureWeek_WhenSearchingCandidates_ThenOnlyEligibleGamesComeBackInKickoffOrder()
    {
        (_, HttpClient client, _) = await CreateCommishLeagueAsync();

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/seasons/{FixtureGameData.SeasonYear}/weeks/{FixtureGameData.Week}/games");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        GameCandidate[]? candidates = await response.Content.ReadFromJsonAsync<GameCandidate[]>();

        candidates.Should().NotBeEmpty();
        candidates.Should().Contain(c => c.HomeTeam.School == "Michigan" && c.AwayTeam.School == "Texas");
        candidates.Should().NotContain(c => c.HomeTeam.School == "Penn State", "Youngstown State is FCS");
        candidates.Should().NotContain(c => c.HomeTeam.School == "Boise State", "that game kicks off on Friday Eastern");
        candidates!.Select(c => c.KickoffUtc).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GivenASearchTerm_WhenSearchingCandidates_ThenOnlyMatchingGamesComeBack()
    {
        (_, HttpClient client, _) = await CreateCommishLeagueAsync();

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/seasons/{FixtureGameData.SeasonYear}/weeks/{FixtureGameData.Week}/games?search=Michigan");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        GameCandidate[]? candidates = await response.Content.ReadFromJsonAsync<GameCandidate[]>();

        candidates.Should().NotBeEmpty();
        candidates.Should().OnlyContain(c =>
            c.HomeTeam.School.Contains("Michigan") || c.AwayTeam.School.Contains("Michigan"));
    }

    [Fact]
    public async Task GivenACallerWhoCommissionsNoLeague_WhenSearchingCandidates_ThenItIs403()
    {
        await FixtureGameData.EnsureSeededAsync(_fixture.Factory);
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        string route = $"/api/seasons/{FixtureGameData.SeasonYear}/weeks/{FixtureGameData.Week}/games";

        using HttpResponseMessage anonymous = await _fixture.Factory.CreateClient().GetAsync(route);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using HttpResponseMessage member = await _fixture.Factory.CreateClientAs(scenario.MemberUserId).GetAsync(route);
        member.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using HttpResponseMessage commissioner =
            await _fixture.Factory.CreateClientAs(scenario.CommissionerUserId).GetAsync(route);
        commissioner.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenTwoActiveGames_WhenRemovingTheEarlierOne_ThenLockAtUtcMovesToTheRemainingKickoff()
    {
        (League league, HttpClient client, _) = await CreateCommishLeagueAsync();
        Guid earlyGameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700002);  // Maryland / Rutgers, 16:00Z
        Guid lateGameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700001);   // Michigan / Texas, 19:30Z

        await client.PostAsJsonAsync($"/api/leagues/{league.Id}/weeks/7/gameset/games", new AddGameRequest(earlyGameId));
        await client.PostAsJsonAsync($"/api/leagues/{league.Id}/weeks/7/gameset/games", new AddGameRequest(lateGameId));

        using HttpResponseMessage response = await client.DeleteAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games/{earlyGameId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WeekGameSetResponse? body = await response.Content.ReadFromJsonAsync<WeekGameSetResponse>();
        body!.LockAtUtc.Should().Be(new DateTimeOffset(2026, 10, 17, 19, 30, 0, TimeSpan.Zero));

        DateTime? persisted = await _fixture.Factory.QueryDbAsync(
            db => db.WeekGameSets.Where(s => s.LeagueId == league.Id && s.Week == 7).Select(s => s.LockAtUtc).SingleAsync());
        persisted.Should().Be(new DateTime(2026, 10, 17, 19, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GivenARemovedGame_WhenReAddingItManually_ThenTheExistingRowFlipsBackToManual()
    {
        (League league, HttpClient client, _) = await CreateCommishLeagueAsync();
        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700005);

        await client.PostAsJsonAsync($"/api/leagues/{league.Id}/weeks/7/gameset/games", new AddGameRequest(gameId));
        Guid gameSetGameId = await _fixture.Factory.QueryDbAsync(
            db => db.WeekGameSetGames.Where(r => r.GameId == gameId && r.WeekGameSet!.LeagueId == league.Id).Select(r => r.Id).SingleAsync());

        await client.DeleteAsync($"/api/leagues/{league.Id}/weeks/7/gameset/games/{gameId}");

        using HttpResponseMessage reAddResponse = await client.PostAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games", new AddGameRequest(gameId));
        reAddResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            WeekGameSetGame row = await db.WeekGameSetGames.SingleAsync(r => r.Id == gameSetGameId);
            row.IsRemoved.Should().BeFalse();
            row.Source.Should().Be(GameSetGameSource.Manual);
            row.RemovedReason.Should().BeNull();
        });

        // Only one row for this game exists — re-adding flipped the row rather than inserting a duplicate.
        int count = await _fixture.Factory.QueryDbAsync(
            db => db.WeekGameSetGames.CountAsync(r => r.GameId == gameId && r.WeekGameSet!.LeagueId == league.Id));
        count.Should().Be(1);
    }
}
