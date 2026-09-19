using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets.Events;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <see cref="GameRemovedPickHandler"/> (Feature 04, P4-04): removing a game keeps every pick row
/// on it (D-008), recomputes counts for the whole roster immediately, and flags only the members
/// who actually had a pick on the game that left.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class GameRemovedTests : IAsyncLifetime
{
    private readonly ClockedApp _app;

    public GameRemovedTests(ApiTestFixture fixture)
    {
        _app = new ClockedApp(fixture);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task GivenAGameIsRemoved_ThenThePickRowOnItIsRetained()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        using HttpClient member = _app.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        GameSetGameDto removedGame = scenario.Games[0];
        await PickAsync(member, scenario, removedGame);

        using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage remove = await commish.DeleteAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games/{removedGame.GameId}"))
        {
            remove.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        bool pickStillThere = await _app.Factory.QueryDbAsync(db => db.Picks
            .AsNoTracking()
            .AnyAsync(p => p.MembershipId == scenario.MemberMembershipId
                && p.WeekGameSetGameId == removedGame.GameSetGameId!.Value));
        pickStillThere.Should().BeTrue();
    }

    [Fact]
    public async Task GivenASubmittedMemberWithAPickOnTheRemovedGame_ThenTheyStaySubmittedAndAreFlagged()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        using HttpClient member = _app.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await PickAllAsync(member, scenario);
        await SubmitAsync(member, scenario);

        GameSetGameDto removedGame = scenario.Games[0];
        using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage remove = await commish.DeleteAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games/{removedGame.GameId}"))
        {
            remove.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        WeekSubmission stored = await ReadSubmissionAsync(scenario, scenario.MemberMembershipId);
        stored.Status.Should().Be(SubmissionStatus.Submitted);
        stored.HasUnseenGameChanges.Should().BeTrue();
    }

    [Fact]
    public async Task GivenAMemberWithNoPickOnTheRemovedGame_ThenTheyAreNotFlagged()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        using HttpClient second = _app.Factory.CreateMutatingClientAs(scenario.SecondMemberUserId);
        GameSetGameDto removedGame = scenario.Games[0];
        GameSetGameDto keptGame = scenario.Games[1];
        await PickAsync(second, scenario, keptGame); // never touches the game about to be removed

        using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage remove = await commish.DeleteAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games/{removedGame.GameId}"))
        {
            remove.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        WeekSubmission stored = await ReadSubmissionAsync(scenario, scenario.SecondMemberMembershipId);
        stored.HasUnseenGameChanges.Should().BeFalse();
        stored.Status.Should().Be(SubmissionStatus.InProgress);
    }

    [Fact]
    public async Task GivenAGameIsRemoved_ThenRosterCountsUpdateImmediately()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        using HttpClient member = _app.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await PickAllAsync(member, scenario);
        await SubmitAsync(member, scenario);

        GameSetGameDto removedGame = scenario.Games[0];
        using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage remove = await commish.DeleteAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games/{removedGame.GameId}"))
        {
            remove.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using HttpResponseMessage roster = await commish.GetAsync($"{scenario.PicksRoute}/status");
        MemberStatusRow[]? rows = await roster.Content.ReadFromJsonAsync<MemberStatusRow[]>();
        MemberStatusRow row = rows!.Single(r => r.MembershipId == scenario.MemberMembershipId);
        row.TotalCount.Should().Be(scenario.Games.Length - 1);
        row.PickedCount.Should().Be(scenario.Games.Length - 1);
    }

    [Fact]
    public async Task GivenARemovedGameIsReAdded_ThenTheFlagIsRaisedAgain()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        using HttpClient member = _app.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await PickAllAsync(member, scenario);
        await SubmitAsync(member, scenario);

        GameSetGameDto removedGame = scenario.Games[0];
        using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage remove = await commish.DeleteAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games/{removedGame.GameId}"))
        {
            remove.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using HttpResponseMessage ack = await member.PostAsync($"{scenario.PicksRoute}/me/ack-changes", null);
        ack.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadSubmissionAsync(scenario, scenario.MemberMembershipId)).HasUnseenGameChanges.Should().BeFalse();

        _app.Advance(TimeSpan.FromMinutes(1));
        using (HttpResponseMessage add = await commish.PostAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games",
            new AddGameRequest(removedGame.GameId)))
        {
            add.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        WeekSubmission after = await ReadSubmissionAsync(scenario, scenario.MemberMembershipId);
        after.HasUnseenGameChanges.Should().BeTrue();
        after.Status.Should().Be(SubmissionStatus.InProgress);
    }

    [Fact]
    public async Task GivenALockedWeek_WhenGameRemovedFromSetIsHandled_ThenNothingChanges()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        using HttpClient member = _app.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await PickAllAsync(member, scenario);
        await SubmitAsync(member, scenario);
        WeekSubmission before = await ReadSubmissionAsync(scenario, scenario.MemberMembershipId);

        await scenario.MarkLockedAsync(_app.Factory, _app.NowUtc.UtcDateTime);

        await using AsyncServiceScope scope = _app.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        GameRemovedPickHandler handler = ActivatorUtilities.CreateInstance<GameRemovedPickHandler>(scope.ServiceProvider);

        await handler.HandleAsync(
            new GameRemovedFromSet(
                scenario.LeagueId,
                PickWeekScenario.Week,
                scenario.WeekGameSetId,
                scenario.Games[0].GameSetGameId!.Value,
                scenario.Games[0].GameId,
                "Manual")
            {
                OccurredUtc = _app.NowUtc.UtcDateTime,
            },
            CancellationToken.None);

        WeekSubmission after = await ReadSubmissionAsync(scenario, scenario.MemberMembershipId);
        after.Status.Should().Be(before.Status);
        after.HasUnseenGameChanges.Should().Be(before.HasUnseenGameChanges);
        after.LastChangedUtc.Should().Be(before.LastChangedUtc);
    }

    private async Task<WeekSubmission> ReadSubmissionAsync(PickWeekScenario scenario, Guid membershipId) =>
        await _app.Factory.QueryDbAsync(db => db.WeekSubmissions
            .AsNoTracking()
            .SingleAsync(submission => submission.WeekGameSetId == scenario.WeekGameSetId
                && submission.MembershipId == membershipId));

    private static async Task PickAllAsync(HttpClient client, PickWeekScenario scenario)
    {
        foreach (GameSetGameDto game in scenario.Games)
        {
            await PickAsync(client, scenario, game);
        }
    }

    private static async Task PickAsync(HttpClient client, PickWeekScenario scenario, GameSetGameDto game)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task SubmitAsync(HttpClient client, PickWeekScenario scenario)
    {
        using HttpResponseMessage response = await client.PostAsync($"{scenario.PicksRoute}/me/submit", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
