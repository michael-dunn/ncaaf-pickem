using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// <see cref="SaturdayReminderOneShot"/> (Feature 11 catalog #3): the per-week one-shot due
/// exactly one hour before lock, recomputed whenever <c>LockAtUtc</c> moves (D-034).
/// </summary>
/// <remarks>
/// Each test builds its own <see cref="WeekGameSet"/> with <c>LockAtUtc</c> relative to
/// <see cref="DateTimeOffset.UtcNow"/> — not the Week 7, 2026 fixture calendar, which is a month
/// in the future relative to the run and would never be "due" against the real clock a
/// <see cref="SchedulerHarness"/> tick uses. No fixture games are needed: the one-shot only reads
/// <c>WeekGameSets.LockAtUtc</c>/<c>LockedUtc</c> and <c>WeekSubmissions</c>.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class SaturdayOneShotTests
{
    private readonly ApiTestFixture _fixture;

    public SaturdayOneShotTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenLockIsOverAnHourAway_WhenTicked_ThenTheReminderIsNotDueYet()
    {
        var sender = new FakePushSender();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (League league, WeekGameSet set) = await SeedAsync(now.AddHours(2));
        (_, string endpoint) = await CreateSubscribedMemberAsync(league);

        await using SchedulerHarness harness = Harness(sender);
        await harness.Tick.TickAsync(now, CancellationToken.None);

        sender.SentTo(endpoint).Should().BeEmpty();
        (await harness.ReadRunsAsync($"SaturdayReminder:{set.Id:N}")).Should().BeEmpty();
    }

    [Fact]
    public async Task GivenLockIsExactlyOneHourAway_WhenTicked_ThenUnsubmittedMembersAreNotified()
    {
        var sender = new FakePushSender();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (League league, WeekGameSet set) = await SeedAsync(now.AddHours(1));
        (_, string endpoint) = await CreateSubscribedMemberAsync(league);

        await using SchedulerHarness harness = Harness(sender);
        await harness.Tick.TickAsync(now, CancellationToken.None);

        sender.SentTo(endpoint).Should().ContainSingle();
        (await harness.ReadRunsAsync($"SaturdayReminder:{set.Id:N}")).Should().ContainSingle()
            .Which.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GivenAMemberAlreadySubmitted_WhenTheReminderIsDue_ThenTheyGetNothing()
    {
        var sender = new FakePushSender();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (League league, WeekGameSet set) = await SeedAsync(now.AddHours(1));
        (User submitted, string submittedEndpoint) = await CreateSubscribedMemberAsync(league);
        (_, string unsubmittedEndpoint) = await CreateSubscribedMemberAsync(league);

        await Factory().ExecuteDbAsync(async db =>
        {
            Guid membershipId = await db.Memberships
                .Where(m => m.LeagueId == league.Id && m.UserId == submitted.Id)
                .Select(m => m.Id)
                .SingleAsync();

            db.WeekSubmissions.Add(new WeekSubmission
            {
                MembershipId = membershipId,
                WeekGameSetId = set.Id,
                Status = SubmissionStatus.Submitted,
                SubmittedUtc = DateTime.UtcNow,
                LastChangedUtc = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
        });

        await using SchedulerHarness harness = Harness(sender);
        await harness.Tick.TickAsync(now, CancellationToken.None);

        sender.SentTo(submittedEndpoint).Should().BeEmpty();
        sender.SentTo(unsubmittedEndpoint).Should().ContainSingle();
    }

    [Fact]
    public async Task GivenTheWeekIsLocked_WhenTicked_ThenNothingRunsOrSends()
    {
        var sender = new FakePushSender();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (League league, WeekGameSet set) = await SeedAsync(now.AddHours(1), locked: true);
        (_, string endpoint) = await CreateSubscribedMemberAsync(league);

        await using SchedulerHarness harness = Harness(sender);
        await harness.Tick.TickAsync(now, CancellationToken.None);

        sender.SentTo(endpoint).Should().BeEmpty();
        (await harness.ReadRunsAsync($"SaturdayReminder:{set.Id:N}")).Should().BeEmpty();
    }

    [Fact]
    public async Task GivenTheLockTimeMoves_WhenTicked_ThenTheSameSetRunsAgainAtTheNewTime()
    {
        var sender = new FakePushSender();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        // Lock is three hours out, so the reminder (due one hour before lock) is not due yet.
        (League league, WeekGameSet set) = await SeedAsync(now.AddHours(3));
        (_, string endpoint) = await CreateSubscribedMemberAsync(league);

        await using SchedulerHarness first = Harness(sender);
        await first.Tick.TickAsync(now, CancellationToken.None);

        sender.SentTo(endpoint).Should().BeEmpty("due is two hours out at the original lock time");
        (await first.ReadRunsAsync($"SaturdayReminder:{set.Id:N}")).Should().BeEmpty();

        // The commissioner pulls the lock time in to one hour from now, so the reminder becomes
        // due right now instead of an hour from now.
        await Factory().ExecuteDbAsync(async db =>
        {
            WeekGameSet row = await db.WeekGameSets.SingleAsync(s => s.Id == set.Id);
            row.LockAtUtc = now.AddHours(1).UtcDateTime;
            await db.SaveChangesAsync();
        });

        await using SchedulerHarness second = Harness(sender);
        await second.Tick.TickAsync(now, CancellationToken.None);

        sender.SentTo(endpoint).Should().ContainSingle(
            "the due time was recomputed from the moved LockAtUtc rather than the original one (D-034)");
        (await second.ReadRunsAsync($"SaturdayReminder:{set.Id:N}")).Should().ContainSingle()
            .Which.ScheduledForUtc.Should().BeCloseTo(now.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    private ApiFactory Factory() => _fixture.Factory;

    private SchedulerHarness Harness(FakePushSender sender) =>
        SchedulerHarness.Create(_fixture, services =>
        {
            services.AddSingleton<IPushSender>(sender);
            services.AddScoped<NotificationService>();
            services.AddScoped<SaturdayReminderOneShot>();
            services.AddScoped<IOneShotJob>(sp => sp.GetRequiredService<SaturdayReminderOneShot>());
        });

    private async Task<(League League, WeekGameSet Set)> SeedAsync(DateTimeOffset lockAtUtc, bool locked = false)
    {
        User owner = await Factory().QueryDbAsync(db => TestUsers.CreateUserAsync(db, "Owner"));
        League league = await Factory().QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, owner));

        var set = new WeekGameSet
        {
            Id = Guid.CreateVersion7(),
            LeagueId = league.Id,
            Week = 999,
            UsesOverride = false,
            GeneratedUtc = DateTime.UtcNow,
            LockAtUtc = lockAtUtc.UtcDateTime,
            LockedUtc = locked ? DateTime.UtcNow : null,
        };

        await Factory().ExecuteDbAsync(async db =>
        {
            db.WeekGameSets.Add(set);
            await db.SaveChangesAsync();
        });

        return (league, set);
    }

    private async Task<(User User, string Endpoint)> CreateSubscribedMemberAsync(League league)
    {
        return await Factory().QueryDbAsync(async db =>
        {
            User user = await TestUsers.CreateUserAsync(db, "Member");
            await TestUsers.CreateMembershipAsync(db, league, user);

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
}
