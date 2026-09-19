using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.GameSets.Events;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Infrastructure.Notifications;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The two event-driven notifications (Feature 11 catalog #4/#5): <c>GameAddedToSet</c> only to
/// previously-Submitted members, coalesced into one "N new games" message per regeneration, and
/// <c>GameRemovedFromSet</c> only to members with a pick on the removed game.
/// </summary>
/// <remarks>
/// Drives the real <c>GameSetService</c> HTTP endpoints against the Week 7, 2026 fixture so the
/// real <c>DomainEventCollector</c>/<c>IDomainEventDispatcher</c> (P2-03) fires the handlers this
/// task registers, rather than raising the events by hand.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class EventNotificationTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly FakePushSender _sender = new();
    private ApiFactory? _factory;

    public EventNotificationTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private ApiFactory Factory => _factory
        ?? throw new InvalidOperationException("The test has not been initialized.");

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(
            _fixture.Database.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(_sender);
            });

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task GivenSubmittedMembers_WhenGamesAreAddedByRegeneration_ThenEachGetsOneCoalescedMessage()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();

        (User submitted, string submittedEndpoint) = await CreateSubscribedMemberAsync(league);
        (_, string inProgressEndpoint) = await CreateSubscribedMemberAsync(league);

        await PutRulesAsync(client, league.Id, [Top25Rule()]);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        Guid setId = await Factory.QueryDbAsync(db => db.WeekGameSets
            .Where(s => s.LeagueId == league.Id && s.Week == 7)
            .Select(s => s.Id)
            .SingleAsync());

        // The submitted member submitted before the regeneration that is about to add games.
        await SetSubmissionAsync(league, submitted, setId, SubmissionStatus.Submitted, DateTime.UtcNow.AddMinutes(-10));

        Guid oklahomaId = await FixtureGameData.GetTeamIdAsync(Factory, 900111);
        Guid ohioStateId = await FixtureGameData.GetTeamIdAsync(Factory, 900105);

        // Adding two Team rules on top of the existing Top25 rule adds two new games and removes
        // none, so the diff is a pure add of two games in one regeneration.
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{league.Id}/gameset-rules",
            new[] { Top25Rule(), TeamRule(oklahomaId), TeamRule(ohioStateId) });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage regenerate = await client.PostAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);
        regenerate.StatusCode.Should().Be(HttpStatusCode.OK);

        FakePushSender.SentPush push = _sender.SentTo(submittedEndpoint).Should().ContainSingle(
            "the submitted member gets exactly one message, coalescing both added games").Subject;

        push.Payload.Body.Should().Be("2 new games were added to Week 7. Update your picks.");
        push.Payload.Url.Should().Be($"/leagues/{league.Id}/weeks/7/picks");

        _sender.SentTo(inProgressEndpoint).Should().BeEmpty("only previously-Submitted members are notified");
    }

    [Fact]
    public async Task GivenAMemberWithAPickOnAGame_WhenTheGameIsManuallyRemoved_ThenTheyAreNotifiedByTeamName()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        (User picker, string pickerEndpoint) = await CreateSubscribedMemberAsync(league);
        (_, string otherEndpoint) = await CreateSubscribedMemberAsync(league);

        await PutRulesAsync(client, league.Id, [Top25Rule()]);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        Guid michiganTexasGameId = await FixtureGameData.GetGameIdAsync(Factory, 700001);
        Guid michiganId = await FixtureGameData.GetTeamIdAsync(Factory, 900101);

        Guid weekGameSetGameId = await Factory.QueryDbAsync(db => db.WeekGameSetGames
            .Where(g => g.GameId == michiganTexasGameId && g.WeekGameSet!.LeagueId == league.Id)
            .Select(g => g.Id)
            .SingleAsync());

        await Factory.ExecuteDbAsync(async db =>
        {
            Guid membershipId = await db.Memberships
                .Where(m => m.LeagueId == league.Id && m.UserId == picker.Id)
                .Select(m => m.Id)
                .SingleAsync();

            db.Picks.Add(new Pick
            {
                Id = Guid.CreateVersion7(),
                MembershipId = membershipId,
                WeekGameSetGameId = weekGameSetGameId,
                PickedTeamId = michiganId,
                UpdatedUtc = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
        });

        using HttpResponseMessage remove = await client.DeleteAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games/{michiganTexasGameId}");
        remove.StatusCode.Should().Be(HttpStatusCode.OK);

        FakePushSender.SentPush push = _sender.SentTo(pickerEndpoint).Should().ContainSingle().Subject;
        push.Payload.Body.Should().Be("Texas vs Michigan was removed from Week 7. Your other picks are unchanged.");

        _sender.SentTo(otherEndpoint).Should().BeEmpty("only a member with a pick on the removed game is notified");
    }

    /// <summary>
    /// A removed membership keeps its <c>Picks</c> rows (they are the locked-week history), but it
    /// is no longer in the league, so it is never a notification recipient.
    /// </summary>
    [Fact]
    public async Task GivenARemovedMemberWithAPickOnAGame_WhenTheGameIsRemoved_ThenTheyAreNotNotified()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        (User former, string formerEndpoint) = await CreateSubscribedMemberAsync(league, removed: true);
        (User active, string activeEndpoint) = await CreateSubscribedMemberAsync(league);

        await PutRulesAsync(client, league.Id, [Top25Rule()]);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        Guid michiganTexasGameId = await FixtureGameData.GetGameIdAsync(Factory, 700001);
        Guid michiganId = await FixtureGameData.GetTeamIdAsync(Factory, 900101);

        Guid weekGameSetGameId = await Factory.QueryDbAsync(db => db.WeekGameSetGames
            .Where(g => g.GameId == michiganTexasGameId && g.WeekGameSet!.LeagueId == league.Id)
            .Select(g => g.Id)
            .SingleAsync());

        foreach (User picker in new[] { former, active })
        {
            await Factory.ExecuteDbAsync(async db =>
            {
                Guid membershipId = await db.Memberships
                    .Where(m => m.LeagueId == league.Id && m.UserId == picker.Id)
                    .Select(m => m.Id)
                    .SingleAsync();

                db.Picks.Add(new Pick
                {
                    Id = Guid.CreateVersion7(),
                    MembershipId = membershipId,
                    WeekGameSetGameId = weekGameSetGameId,
                    PickedTeamId = michiganId,
                    UpdatedUtc = DateTime.UtcNow,
                });

                await db.SaveChangesAsync();
            });
        }

        using HttpResponseMessage remove = await client.DeleteAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games/{michiganTexasGameId}");
        remove.StatusCode.Should().Be(HttpStatusCode.OK);

        _sender.SentTo(activeEndpoint).Should().ContainSingle();
        _sender.SentTo(formerEndpoint).Should().BeEmpty("a removed membership is no longer in the league");

        bool formerHasLogRow = await Factory.QueryDbAsync(db => db.NotificationLog.AnyAsync(
            row => row.UserId == former.Id && row.Type == NotificationType.GameRemoved));
        formerHasLogRow.Should().BeFalse();
    }

    [Fact]
    public async Task GivenALockedWeek_WhenTheGamesAddedHandlerRuns_ThenNothingIsSent()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        (User submitted, string endpoint) = await CreateSubscribedMemberAsync(league);

        await PutRulesAsync(client, league.Id, [Top25Rule()]);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        Guid setId = await Factory.QueryDbAsync(db => db.WeekGameSets
            .Where(s => s.LeagueId == league.Id && s.Week == 7)
            .Select(s => s.Id)
            .SingleAsync());

        await SetSubmissionAsync(league, submitted, setId, SubmissionStatus.Submitted, DateTime.UtcNow.AddMinutes(-10));

        Guid weekGameSetGameId = await Factory.QueryDbAsync(db => db.WeekGameSetGames
            .Where(g => g.WeekGameSetId == setId)
            .Select(g => g.Id)
            .FirstAsync());

        await Factory.ExecuteDbAsync(async db =>
        {
            WeekGameSet row = await db.WeekGameSets.SingleAsync(s => s.Id == setId);
            row.LockedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });

        var domainEvent = new GameAddedToSet(league.Id, 7, setId, [weekGameSetGameId], "Manual")
        {
            OccurredUtc = DateTime.UtcNow,
        };

        await using AsyncServiceScope scope = Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        GamesAddedNotificationHandler handler = scope.ServiceProvider
            .GetServices<IDomainEventHandler<GameAddedToSet>>()
            .OfType<GamesAddedNotificationHandler>()
            .Single();

        await handler.HandleAsync(domainEvent, CancellationToken.None);

        _sender.SentTo(endpoint).Should().BeEmpty("a locked week's own generation is a no-op, and a handler must honor that too");
    }

    private static GameSetRuleDto Top25Rule() => new(null, GameSetRuleType.Top25, null, null, null, null, false, 0);

    private static GameSetRuleDto TeamRule(Guid teamId) => new(null, GameSetRuleType.Team, null, null, teamId, null, false, 0);

    private static async Task PutRulesAsync(HttpClient client, Guid leagueId, GameSetRuleDto[] rules)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync($"/api/leagues/{leagueId}/gameset-rules", rules);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<(League League, HttpClient Client)> CreateCommishLeagueAsync()
    {
        await FixtureGameData.EnsureSeededAsync(Factory);

        User commissioner = await Factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "Commish"));
        League league = await Factory.QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, commissioner));
        await Factory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));

        HttpClient client = Factory.CreateMutatingClientAs(commissioner.Id);
        return (league, client);
    }

    private async Task<(User User, string Endpoint)> CreateSubscribedMemberAsync(League league, bool removed = false)
    {
        return await Factory.QueryDbAsync(async db =>
        {
            User user = await TestUsers.CreateUserAsync(db, "Member");
            await TestUsers.CreateMembershipAsync(db, league, user, MembershipRole.Member, removed);

            string endpoint = $"https://push.example/send/{Guid.CreateVersion7():N}";
            db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                Endpoint = endpoint,
                P256dh = "p256dh",
                Auth = "auth",
                CreatedUtc = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
            return (user, endpoint);
        });
    }

    private async Task SetSubmissionAsync(
        League league, User user, Guid weekGameSetId, SubmissionStatus status, DateTime submittedUtc)
    {
        await Factory.ExecuteDbAsync(async db =>
        {
            Guid membershipId = await db.Memberships
                .Where(m => m.LeagueId == league.Id && m.UserId == user.Id)
                .Select(m => m.Id)
                .SingleAsync();

            db.WeekSubmissions.Add(new WeekSubmission
            {
                MembershipId = membershipId,
                WeekGameSetId = weekGameSetId,
                Status = status,
                SubmittedUtc = submittedUtc,
                LastChangedUtc = submittedUtc,
            });

            await db.SaveChangesAsync();
        });
    }
}
