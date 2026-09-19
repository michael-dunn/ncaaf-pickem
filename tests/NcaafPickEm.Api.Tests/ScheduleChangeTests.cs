using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <see cref="ScheduleChangeHandler"/> (Feature 02, P3-04) driven through the real
/// <c>GameScheduleChanged</c> pipeline: <see cref="LiveScoreApplyService"/> raises the event,
/// the real <c>IDomainEventDispatcher</c> delivers it to the handler, exactly as production does.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class ScheduleChangeTests
{
    private static readonly DateOnly Saturday = new(2026, 10, 17);

    private readonly ApiTestFixture _fixture;

    public ScheduleChangeTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(League League, HttpClient Client, Guid GameId)> SeedLeagueWithGameAsync()
    {
        await FixtureGameData.EnsureSeededAsync(_fixture.Factory);

        User commissioner = await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "ScheduleCommish"));
        League league = await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, commissioner));
        await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));

        HttpClient client = _fixture.Factory.CreateMutatingClientAs(commissioner.Id);

        // Game 700002 (ESPN event 401900001): unranked, so a manual add is what puts it in the
        // set - LiveScoreApplyTests already drives this exact game through Scheduled -> Postponed
        // -> Scheduled via the fixture's snapshot 1. Reset it back to Scheduled first: the
        // database is shared across the run, and an earlier test (here or in
        // LiveScoreApplyTests) may have left it Postponed, which the manual-add eligibility
        // check would then reject.
        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700002);
        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            await db.Games.Where(g => g.Id == gameId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(g => g.Status, GameStatus.Scheduled));
        });

        using HttpResponseMessage addResponse = await client.PostAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games",
            new AddGameRequest(gameId));
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        return (league, client, gameId);
    }

    private async Task<LiveScoreUpdate> ScheduledUpdateAsync()
    {
        var state = new FixtureSnapshotState();
        state.Set(1);
        IReadOnlyList<LiveScoreUpdate> updates = await new FixtureLiveScoreProvider(state).GetScoresAsync(Saturday);
        return updates.Single(u => u.SourceEventId == "401900001");
    }

    private async Task ApplyAsync(LiveScoreUpdate update)
    {
        await using AsyncServiceScope scope = _fixture.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        LiveScoreApplyService service = scope.ServiceProvider.GetRequiredService<LiveScoreApplyService>();
        await service.ApplyAsync(Saturday, [update]);
    }

    private async Task<NeedsVoidReviewItem[]> ListNeedsVoidReviewAsync(Guid leagueId)
    {
        await using AsyncServiceScope scope = _fixture.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        GameSetService service = scope.ServiceProvider.GetRequiredService<GameSetService>();
        return await service.ListNeedsVoidReviewAsync(leagueId, CancellationToken.None);
    }

    private Task<WeekGameSetGame> RowAsync(Guid leagueId, Guid gameId) =>
        _fixture.Factory.QueryDbAsync(db => db.WeekGameSetGames
            .AsNoTracking()
            .Include(r => r.WeekGameSet)
            .Include(r => r.Game)
            .Where(r => r.WeekGameSet!.LeagueId == leagueId && r.GameId == gameId)
            .SingleAsync());

    [Fact]
    public async Task GivenAGameInAnUnlockedWeek_WhenItIsPostponed_ThenItIsRemovedAndLockAtIsRecomputed()
    {
        (League league, HttpClient client, Guid gameId) = await SeedLeagueWithGameAsync();

        LiveScoreUpdate scheduled = await ScheduledUpdateAsync();
        LiveScoreUpdate postponed = scheduled with { Status = GameStatus.Postponed, RawStatusName = "STATUS_POSTPONED" };

        await ApplyAsync(postponed);

        WeekGameSetGame row = await RowAsync(league.Id, gameId);
        row.IsRemoved.Should().BeTrue();
        row.RemovedReason.Should().Be("Schedule change");

        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{league.Id}/weeks/7/gameset");
        WeekGameSetResponse body = (await response.Content.ReadFromJsonAsync<WeekGameSetResponse>())!;
        body.Games.Should().BeEmpty(); // it was the only game in the set
        body.LockAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GivenAPostponedGame_WhenItReturnsToScheduled_ThenItIsRestored()
    {
        (League league, HttpClient client, Guid gameId) = await SeedLeagueWithGameAsync();

        LiveScoreUpdate scheduled = await ScheduledUpdateAsync();
        LiveScoreUpdate postponed = scheduled with { Status = GameStatus.Postponed, RawStatusName = "STATUS_POSTPONED" };

        await ApplyAsync(postponed);
        (await RowAsync(league.Id, gameId)).IsRemoved.Should().BeTrue();

        await ApplyAsync(scheduled);

        WeekGameSetGame restored = await RowAsync(league.Id, gameId);
        restored.IsRemoved.Should().BeFalse();
        restored.RemovedReason.Should().BeNull();

        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{league.Id}/weeks/7/gameset");
        WeekGameSetResponse body = (await response.Content.ReadFromJsonAsync<WeekGameSetResponse>())!;
        body.Games.Should().ContainSingle();
        body.LockAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GivenAManuallyRemovedGame_WhenItIsPostponed_ThenTheManualRemovalReasonIsLeftAlone()
    {
        (League league, HttpClient client, Guid gameId) = await SeedLeagueWithGameAsync();

        using HttpResponseMessage removeResponse = await client.DeleteAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games/{gameId}");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        WeekGameSetGame manuallyRemoved = await RowAsync(league.Id, gameId);
        manuallyRemoved.RemovedReason.Should().Be("Manual");

        LiveScoreUpdate scheduled = await ScheduledUpdateAsync();
        LiveScoreUpdate postponed = scheduled with { Status = GameStatus.Postponed, RawStatusName = "STATUS_POSTPONED" };
        await ApplyAsync(postponed);

        // Postponing an already (manually) removed game touches nothing - the row was not
        // active to begin with.
        WeekGameSetGame afterPostponed = await RowAsync(league.Id, gameId);
        afterPostponed.IsRemoved.Should().BeTrue();
        afterPostponed.RemovedReason.Should().Be("Manual");

        await ApplyAsync(scheduled);

        // Coming back to Scheduled must not resurrect a manual removal - only the handler's own
        // "Schedule change" removals get restored.
        WeekGameSetGame afterReinstated = await RowAsync(league.Id, gameId);
        afterReinstated.IsRemoved.Should().BeTrue();
        afterReinstated.RemovedReason.Should().Be("Manual");
    }

    [Fact]
    public async Task GivenALockedWeek_WhenItsGameIsPostponed_ThenNothingIsRemovedAndItAppearsInNeedsVoidReview()
    {
        (League league, HttpClient client, Guid gameId) = await SeedLeagueWithGameAsync();

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            WeekGameSet set = await db.WeekGameSets.SingleAsync(s => s.LeagueId == league.Id && s.Week == 7);
            set.LockedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });

        LiveScoreUpdate scheduled = await ScheduledUpdateAsync();
        LiveScoreUpdate postponed = scheduled with { Status = GameStatus.Postponed, RawStatusName = "STATUS_POSTPONED" };
        await ApplyAsync(postponed);

        WeekGameSetGame row = await RowAsync(league.Id, gameId);
        row.IsRemoved.Should().BeFalse("a locked week's rows are left exactly as they are - P5-02's void flow owns them from here");
        row.IsVoided.Should().BeFalse();

        NeedsVoidReviewItem[] review = await ListNeedsVoidReviewAsync(league.Id);
        review.Should().ContainSingle(r => r.GameId == gameId && r.LeagueId == league.Id && r.Week == 7);

        // Re-applying the identical postponed payload is a no-op upstream (LiveScoreApplyService
        // never re-raises GameScheduleChanged for a status that has not changed), so the handler
        // is never asked to process the same transition twice.
        await ApplyAsync(postponed);
        review = await ListNeedsVoidReviewAsync(league.Id);
        review.Should().ContainSingle(r => r.GameId == gameId);
    }
}
