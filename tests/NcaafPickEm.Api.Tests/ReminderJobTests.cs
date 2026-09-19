using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Notifications;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P7-03's Friday jobs (Feature 11, <c>04-Domain-Algorithms.md</c> section 11): the member
/// reminder and the commissioner unsubmitted summary, both evaluated at send time.
/// </summary>
/// <remarks>
/// The clock is pinned to Friday 8:00 PM ET (2026-10-16, inside the Week 7, 2026 fixture window),
/// which the fixture calendar resolves as the league's current week — see
/// <c>FixtureSeasonWeekSource</c> and <c>AGENT-NOTES.md</c> "Running against fixtures".
/// Jobs are driven by calling <c>RunAsync</c> directly (resolved from the real DI container so
/// registration is exercised too), except one test that drives the real cron through
/// <see cref="SchedulerHarness"/>.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class ReminderJobTests : IAsyncLifetime
{
    /// <summary>Friday 2026-10-16, 20:00 ET = 2026-10-17T00:00:00Z (EDT, UTC-4).</summary>
    private static readonly DateTimeOffset FridayEightPmEasternUtc = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Same Friday, 21:00 ET.</summary>
    private static readonly DateTimeOffset FridayNinePmEasternUtc = new(2026, 10, 17, 1, 0, 0, TimeSpan.Zero);

    private readonly ApiTestFixture _fixture;
    private readonly FakePushSender _sender = new();
    private readonly FixedTimeProvider _clock = new(FridayEightPmEasternUtc);
    private ApiFactory? _factory;

    public ReminderJobTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private ApiFactory Factory => _factory
        ?? throw new InvalidOperationException("The test has not been initialized.");

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(
            _fixture.Database.ConnectionString,
            timeProvider: _clock,
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
    public async Task GivenMembersAtEveryStatus_WhenTheFridayReminderRuns_ThenOnlyTheNonSubmittedAreNotified()
    {
        League league = await CreateLeagueAsync();
        WeekGameSet set = await SeedWeekSetAsync(league.Id, 7, [700001, 700006, 700007]);

        (User notStarted, string notStartedEndpoint) = await CreateSubscribedMemberAsync(league, "no-row");
        (User inProgress, string inProgressEndpoint) = await CreateSubscribedMemberAsync(league, "in-progress");
        (User submitted, string submittedEndpoint) = await CreateSubscribedMemberAsync(league, "submitted");

        await SetSubmissionAsync(league, inProgress, set.Id, SubmissionStatus.InProgress);
        await SetSubmissionAsync(league, submitted, set.Id, SubmissionStatus.Submitted);
        // notStarted intentionally has no WeekSubmissions row.

        await RunFridayMemberReminderAsync();

        _sender.SentTo(notStartedEndpoint).Should().ContainSingle();
        _sender.SentTo(inProgressEndpoint).Should().ContainSingle();
        _sender.SentTo(submittedEndpoint).Should().BeEmpty();

        bool submittedHasLogRow = await Factory.QueryDbAsync(db => db.NotificationLog.AnyAsync(
            row => row.UserId == submitted.Id && row.Type == NotificationType.FridayReminder));
        submittedHasLogRow.Should().BeFalse("a Submitted member is never a recipient at all");
    }

    [Fact]
    public async Task GivenAMemberWhoSubmittedOneMinuteBeforeTheJobRuns_WhenTheFridayReminderRuns_ThenTheyGetNothing()
    {
        League league = await CreateLeagueAsync();
        WeekGameSet set = await SeedWeekSetAsync(league.Id, 7, [700001]);
        (User member, string endpoint) = await CreateSubscribedMemberAsync(league, "just-submitted");

        await SetSubmissionAsync(
            league, member, set.Id, SubmissionStatus.Submitted, FridayEightPmEasternUtc.AddMinutes(-1).UtcDateTime);

        await RunFridayMemberReminderAsync();

        _sender.SentTo(endpoint).Should().BeEmpty("status is checked at send time, not scheduled in advance");
    }

    [Fact]
    public async Task GivenNoGameSetForTheCurrentWeek_WhenTheFridayReminderRuns_ThenNothingIsSent()
    {
        League league = await CreateLeagueAsync();
        (_, string endpoint) = await CreateSubscribedMemberAsync(league, "no-set");

        await RunFridayMemberReminderAsync();

        _sender.SentTo(endpoint).Should().BeEmpty();
    }

    [Fact]
    public async Task GivenTheCurrentWeekIsLocked_WhenTheFridayReminderRuns_ThenNothingIsSent()
    {
        League league = await CreateLeagueAsync();
        WeekGameSet set = await SeedWeekSetAsync(league.Id, 7, [700001]);
        await Factory.ExecuteDbAsync(async db =>
        {
            WeekGameSet row = await db.WeekGameSets.SingleAsync(s => s.Id == set.Id);
            row.LockedUtc = FridayEightPmEasternUtc.UtcDateTime;
            await db.SaveChangesAsync();
        });

        (_, string endpoint) = await CreateSubscribedMemberAsync(league, "locked");

        await RunFridayMemberReminderAsync();

        _sender.SentTo(endpoint).Should().BeEmpty();
    }

    [Fact]
    public async Task GivenTheJobRunsTwiceForTheSameWeek_WhenSecondRun_ThenTheSecondSendIsSkipped()
    {
        League league = await CreateLeagueAsync();
        await SeedWeekSetAsync(league.Id, 7, [700001]);
        (_, string endpoint) = await CreateSubscribedMemberAsync(league, "twice");

        await RunFridayMemberReminderAsync();
        await RunFridayMemberReminderAsync();

        _sender.SentTo(endpoint).Should().ContainSingle("the filtered unique index makes the retry a no-op");
    }

    [Fact]
    public async Task GivenNoOneUnsubmitted_WhenTheCommissionerSummaryRuns_ThenNothingIsSent()
    {
        League league = await CreateLeagueAsync();
        WeekGameSet set = await SeedWeekSetAsync(league.Id, 7, [700001]);
        (User commissioner, string commishEndpoint) = await CreateSubscribedMemberAsync(
            league, "commish", MembershipRole.Commissioner);
        (User member, _) = await CreateSubscribedMemberAsync(league, "all-submitted");
        await SetSubmissionAsync(league, commissioner, set.Id, SubmissionStatus.Submitted);
        await SetSubmissionAsync(league, member, set.Id, SubmissionStatus.Submitted);

        await RunCommissionerSummaryAsync();

        _sender.SentTo(commishEndpoint).Should().BeEmpty();
    }

    [Fact]
    public async Task GivenSomeoneUnsubmitted_WhenTheCommissionerSummaryRuns_ThenTheCommissionerIsToldWhoByName()
    {
        League league = await CreateLeagueAsync();
        WeekGameSet set = await SeedWeekSetAsync(league.Id, 7, [700001]);
        (User commissioner, string commishEndpoint) = await CreateSubscribedMemberAsync(
            league, "commish2", MembershipRole.Commissioner);
        await CreateSubscribedMemberAsync(league, "Riley");
        await SetSubmissionAsync(league, commissioner, set.Id, SubmissionStatus.Submitted);

        await RunCommissionerSummaryAsync();

        FakePushSender.SentPush push = _sender.SentTo(commishEndpoint).Should().ContainSingle().Subject;
        push.Payload.Body.Should().Contain("1 members haven't submitted Week 7 picks");
        push.Payload.Body.Should().Contain("Riley");
    }

    /// <summary>
    /// Drives the actual cron (not a fake) through the real scheduler tick, proving
    /// <see cref="FridayMemberReminderJob"/> is registered and its cron expression fires at
    /// Friday 20:00 Eastern — the "registration + cron" case the card asks for.
    /// </summary>
    [Fact]
    public async Task GivenTheRealSchedulerTicksAtFridayEightPmEastern_WhenTicked_ThenTheRealJobRunsAndSends()
    {
        League league = await CreateLeagueAsync();
        await SeedWeekSetAsync(league.Id, 7, [700001]);
        (_, string endpoint) = await CreateSubscribedMemberAsync(league, "cron");

        await using AsyncServiceScope scope = Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        SchedulerTick tick = scope.ServiceProvider.GetRequiredService<SchedulerTick>();

        await tick.TickAsync(FridayEightPmEasternUtc, CancellationToken.None);

        _sender.SentTo(endpoint).Should().ContainSingle();
    }

    private async Task RunFridayMemberReminderAsync()
    {
        await using AsyncServiceScope scope = Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        FridayMemberReminderJob job = scope.ServiceProvider.GetServices<IScheduledJob>()
            .OfType<FridayMemberReminderJob>().Single();

        await job.RunAsync(FridayEightPmEasternUtc, CancellationToken.None);
    }

    private async Task RunCommissionerSummaryAsync()
    {
        await using AsyncServiceScope scope = Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        FridayCommissionerSummaryJob job = scope.ServiceProvider.GetServices<IScheduledJob>()
            .OfType<FridayCommissionerSummaryJob>().Single();

        await job.RunAsync(FridayNinePmEasternUtc, CancellationToken.None);
    }

    private async Task<League> CreateLeagueAsync()
    {
        await FixtureGameData.EnsureSeededAsync(Factory);

        return await Factory.QueryDbAsync(async db =>
        {
            User owner = await TestUsers.CreateUserAsync(db, "Owner");
            return await TestUsers.CreateLeagueAsync(db, owner);
        });
    }

    private async Task<(User User, string Endpoint)> CreateSubscribedMemberAsync(
        League league,
        string name,
        MembershipRole role = MembershipRole.Member)
    {
        return await Factory.QueryDbAsync(async db =>
        {
            User user = await TestUsers.CreateUserAsync(db, name);
            await TestUsers.CreateMembershipAsync(db, league, user, role);

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

    /// <summary>
    /// Inserts a <c>WeekGameSets</c> row directly (bypassing <c>GameSetService</c>, which P4-01's
    /// picks endpoints do not need to exist for this task) plus one <c>WeekGameSetGames</c> row
    /// per fixture <c>cfbdGameId</c>, with <c>LockAtUtc</c> the earliest of their kickoffs.
    /// </summary>
    private async Task<WeekGameSet> SeedWeekSetAsync(Guid leagueId, int week, long[] cfbdGameIds)
    {
        var set = new WeekGameSet
        {
            Id = Guid.CreateVersion7(),
            LeagueId = leagueId,
            Week = week,
            UsesOverride = false,
            GeneratedUtc = DateTime.UtcNow,
        };

        await Factory.ExecuteDbAsync(async db =>
        {
            db.WeekGameSets.Add(set);
            await db.SaveChangesAsync();

            DateTime? earliestKickoff = null;

            foreach (long cfbdGameId in cfbdGameIds)
            {
                Guid gameId = await db.Games.Where(g => g.CfbdGameId == cfbdGameId).Select(g => g.Id).FirstAsync();
                DateTime kickoffUtc = await db.Games.Where(g => g.Id == gameId).Select(g => g.KickoffUtc).FirstAsync();
                earliestKickoff = earliestKickoff is null || kickoffUtc < earliestKickoff ? kickoffUtc : earliestKickoff;

                db.WeekGameSetGames.Add(new WeekGameSetGame
                {
                    Id = Guid.CreateVersion7(),
                    WeekGameSetId = set.Id,
                    GameId = gameId,
                    Source = GameSetGameSource.Rule,
                    ResolvedPointValue = 10,
                });
            }

            set.LockAtUtc = earliestKickoff;
            await db.SaveChangesAsync();
        });

        return set;
    }

    private async Task SetSubmissionAsync(
        League league,
        User user,
        Guid weekGameSetId,
        SubmissionStatus status,
        DateTime? submittedUtc = null)
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
                SubmittedUtc = status == SubmissionStatus.Submitted ? submittedUtc ?? DateTime.UtcNow : null,
                LastChangedUtc = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
        });
    }
}
