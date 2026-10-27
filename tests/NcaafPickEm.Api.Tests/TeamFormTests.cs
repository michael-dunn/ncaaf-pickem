using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.TeamForm;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Feature 17 (D-194): <c>GET .../weeks/{week}/team-form</c> gives every team in the week's set its
/// last three Final games of the season before its game, newest first and told from its side. The
/// refusal side of the authorization matrix is <see cref="GeneratedAuthMatrixTests"/>.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class TeamFormTests
{
    private readonly ApiTestFixture _fixture;

    public TeamFormTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenEarlierGames_WhenAMemberReadsTeamForm_ThenEachSetTeamGetsItsLastThreeFinalGamesThisSeason()
    {
        ApiFactory factory = _fixture.PinnedFactory;
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(factory);
        GameSetGameDto setGame = scenario.Games[0];
        Guid team = setGame.HomeTeam.TeamId;
        Guid opponent = scenario.Games[1].HomeTeam.TeamId;
        DateTime kickoff = setGame.KickoffUtc.UtcDateTime;

        Game oneWeekAgo = NewGame(team, opponent, kickoff.AddDays(-7), GameStatus.Final, 24, 21);
        Game twoWeeksAgo = NewGame(opponent, team, kickoff.AddDays(-14), GameStatus.Final, 31, 10);
        Game threeWeeksAgo = NewGame(team, opponent, kickoff.AddDays(-21), GameStatus.Final, 38, 7);
        Game fourWeeksAgo = NewGame(team, opponent, kickoff.AddDays(-28), GameStatus.Final, 14, 13);
        Game notFinal = NewGame(team, opponent, kickoff.AddDays(-3), GameStatus.Scheduled, null, null);
        Game lastSeason = NewGame(team, opponent, kickoff.AddDays(-2), GameStatus.Final, 50, 0, seasonYear: FixtureGameData.SeasonYear - 1);
        Game[] inserted = [oneWeekAgo, twoWeeksAgo, threeWeeksAgo, fourWeeksAgo, notFinal, lastSeason];

        await factory.ExecuteDbAsync(async db =>
        {
            db.Games.AddRange(inserted);
            await db.SaveChangesAsync();
        });

        try
        {
            using HttpClient member = factory.CreateClientAs(scenario.MemberUserId);
            using HttpResponseMessage response = await member.GetAsync(
                $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/team-form");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            WeekTeamFormResponse? body = await response.Content.ReadFromJsonAsync<WeekTeamFormResponse>();

            body!.Teams.Select(t => t.TeamId).Should().BeEquivalentTo(
                scenario.Games.SelectMany(g => new[] { g.HomeTeam.TeamId, g.AwayTeam.TeamId }).Distinct(),
                "every team in the set is listed, with or without recent games");

            RecentGameDto[] form = body.Teams.Single(t => t.TeamId == team).Games;
            form.Select(g => g.GameId).Should().Equal(
                [oneWeekAgo.Id, twoWeeksAgo.Id, threeWeeksAgo.Id],
                "only this season's Final games count, newest first, three at most");

            form[0].Should().BeEquivalentTo(new
            {
                IsHome = true,
                TeamScore = 24,
                OpponentScore = 21,
                Result = FormResult.Win,
            });
            form[0].Opponent.TeamId.Should().Be(opponent);
            form[1].Should().BeEquivalentTo(new
            {
                IsHome = false,
                TeamScore = 10,
                OpponentScore = 31,
                Result = FormResult.Loss,
            });
        }
        finally
        {
            Guid[] ids = [.. inserted.Select(g => g.Id)];
            await factory.ExecuteDbAsync(db => db.Games.Where(g => ids.Contains(g.Id)).ExecuteDeleteAsync());
        }
    }

    [Fact]
    public async Task GivenAWeekOutsideTheLeaguesRange_WhenAMemberReadsTeamForm_ThenItIs404()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await member.GetAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/99/team-form");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenPicksHaveNotOpened_WhenAMemberReadsTeamForm_ThenThereAreNoTeams()
    {
        // Monday 2026-10-12 23:59:59 EDT, one second before week 7's picks open (D-192).
        await using ClockedApp app = new(_fixture, new DateTimeOffset(2026, 10, 13, 3, 59, 59, TimeSpan.Zero));
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(app.Factory);
        using HttpClient member = app.Factory.CreateClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await member.GetAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/team-form");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WeekTeamFormResponse? body = await response.Content.ReadFromJsonAsync<WeekTeamFormResponse>();
        body!.Teams.Should().BeEmpty("the provisional set is the commissioners' preview, as on the picks page");
    }

    private static Game NewGame(
        Guid home,
        Guid away,
        DateTime kickoffUtc,
        GameStatus status,
        int? homeScore,
        int? awayScore,
        int seasonYear = FixtureGameData.SeasonYear)
    {
        DateTimeOffset kickoff = new(kickoffUtc, TimeSpan.Zero);
        return new Game
        {
            Id = Guid.CreateVersion7(),
            CfbdGameId = Random.Shared.NextInt64(80_000_000, 89_999_999),
            SeasonYear = seasonYear,
            Week = 1,
            HomeTeamId = home,
            AwayTeamId = away,
            KickoffUtc = kickoffUtc,
            KickoffEasternDate = DateOnly.FromDateTime(SeasonCalendar.ToEastern(kickoff).DateTime),
            IsSaturdayEastern = SeasonCalendar.IsSaturdayEastern(kickoff),
            IsConferenceGame = false,
            Status = status,
            HomeScore = homeScore,
            AwayScore = awayScore,
        };
    }
}
