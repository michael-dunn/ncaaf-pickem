using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The Feature 11 delivery policy end to end over a scripted transport: 404/410 deletes the
/// subscription and logs Expired, other failures retry three times over fifteen minutes and then
/// log Failed, the weekly reminders go out at most once per week, and the payload on the wire is
/// <c>{ title, body, url, tag }</c>.
/// </summary>
/// <remarks>
/// Runs against its own <see cref="ApiFactory"/> (sharing the collection's database) so
/// <see cref="IPushSender"/> can be replaced without affecting any other test class.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class PushDeliveryTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly FakePushSender _sender = new();
    private ApiFactory? _factory;

    public PushDeliveryTests(ApiTestFixture fixture)
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
    public async Task GivenASubscribedMember_WhenSending_ThenTheRowIsSentAndThePayloadIsCamelCase()
    {
        Recipient recipient = await SeedAsync();

        NotificationResult result = await SendAsync(
            recipient,
            NotificationType.FridayReminder,
            week: 7,
            new PushPayload("You have 3 picks left", "Lock is Saturday at 12:00 PM.", "/leagues/x/picks", "week-7"));

        result.Should().Be(NotificationResult.Sent);

        NotificationLogEntry entry = await SingleLogAsync(recipient, NotificationType.FridayReminder, week: 7);
        entry.Result.Should().Be(NotificationResult.Sent);
        entry.Error.Should().BeNull();
        entry.SubscriptionId.Should().Be(recipient.SubscriptionId);

        PushSubscription subscription = await ReadSubscriptionAsync(recipient.SubscriptionId);
        subscription.LastSuccessUtc.Should().NotBeNull();
        subscription.FailureCount.Should().Be(0);

        FakePushSender.SentPush sent = _sender.SentTo(recipient.Endpoint).Should().ContainSingle().Subject;
        sent.Ttl.Should().Be(PushTtl.Reminder, "the card fixes a one-hour TTL for reminders");

        using JsonDocument payload = JsonDocument.Parse(sent.Json);
        payload.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["title", "body", "url", "tag"]);
        payload.RootElement.GetProperty("title").GetString().Should().Be("You have 3 picks left");
        payload.RootElement.GetProperty("url").GetString().Should().Be("/leagues/x/picks");
        payload.RootElement.GetProperty("tag").GetString().Should().Be("week-7");
    }

    [Fact]
    public async Task GivenAnEventNotification_WhenSending_ThenTheTtlIsFourHours()
    {
        Recipient recipient = await SeedAsync();

        await SendAsync(recipient, NotificationType.GamesAdded, week: 7, Payload());

        _sender.SentTo(recipient.Endpoint).Should().ContainSingle()
            .Which.Ttl.Should().Be(PushTtl.Event);
    }

    [Fact]
    public async Task GivenAPushServiceThatAnswers410_WhenSending_ThenTheSubscriptionIsDeletedAndLoggedExpired()
    {
        Recipient recipient = await SeedAsync();
        _sender.Script(recipient.Endpoint, PushSendResult.Gone("Push service returned 410.", 410));

        NotificationResult result = await SendAsync(recipient, NotificationType.SaturdayReminder, week: 7, Payload());

        result.Should().Be(NotificationResult.Expired);

        NotificationLogEntry entry = await SingleLogAsync(recipient, NotificationType.SaturdayReminder, week: 7);
        entry.Result.Should().Be(NotificationResult.Expired);
        entry.Error.Should().Contain("410");
        entry.SubscriptionId.Should().BeNull("the row it pointed at has been deleted");

        bool exists = await Factory.QueryDbAsync(database =>
            database.PushSubscriptions.AnyAsync(subscription => subscription.Id == recipient.SubscriptionId));

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GivenATransientFailure_WhenSending_ThenARetryIsQueuedAndTheRowIsFailed()
    {
        Recipient recipient = await SeedAsync();
        _sender.Script(recipient.Endpoint, PushSendResult.Failed("Push service returned 500.", 500));

        NotificationResult result = await SendAsync(recipient, NotificationType.FridayReminder, week: 8, Payload());

        result.Should().Be(NotificationResult.Failed);

        NotificationLogEntry entry = await SingleLogAsync(recipient, NotificationType.FridayReminder, week: 8);
        entry.Result.Should().Be(NotificationResult.Failed);
        entry.Error.Should().Contain("Retry 1 of 3");

        PushRetry retry = await SingleRetryAsync(entry.Id);
        retry.Attempt.Should().Be(0);
        retry.SubscriptionId.Should().Be(recipient.SubscriptionId);
        retry.NextAttemptUtc.Should().BeCloseTo(retry.CreatedUtc.AddMinutes(1), TimeSpan.FromSeconds(5));

        PushSubscription subscription = await ReadSubscriptionAsync(recipient.SubscriptionId);
        subscription.FailureCount.Should().Be(1);
    }

    [Fact]
    public async Task GivenADueRetry_WhenTheJobAsksWhatIsDue_ThenTheRowIsOffered()
    {
        Recipient recipient = await SeedAsync();
        _sender.Script(recipient.Endpoint, PushSendResult.Failed("Push service returned 500.", 500));

        await SendAsync(recipient, NotificationType.GamesAdded, week: 9, Payload());
        PushRetry retry = await SingleRetryAsync(await SingleLogIdAsync(recipient, NotificationType.GamesAdded, 9));

        IReadOnlyList<OneShotOccurrence> due = await WithJobAsync(job =>
            job.GetDueAsync(DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

        due.Should().Contain(occurrence => occurrence.Key == retry.Id.ToString("N"));
    }

    [Fact]
    public async Task GivenThreeFailedRetries_WhenTheJobRuns_ThenTheRowEndsFailedAndTheRetryIsGone()
    {
        Recipient recipient = await SeedAsync();
        _sender.Script(recipient.Endpoint, PushSendResult.Failed("Push service returned 500.", 500));

        await SendAsync(recipient, NotificationType.FridayReminder, week: 10, Payload());
        Guid logId = await SingleLogIdAsync(recipient, NotificationType.FridayReminder, 10);

        for (int attempt = 1; attempt <= PushRetryPolicy.MaxAttempts; attempt++)
        {
            PushRetry retry = await SingleRetryAsync(logId);
            retry.Attempt.Should().Be(attempt - 1);

            await RunRetryAsync(retry.Id);
        }

        // Original attempt + three retries, and then it gives up.
        _sender.SentTo(recipient.Endpoint).Should().HaveCount(1 + PushRetryPolicy.MaxAttempts);

        NotificationLogEntry entry = await ReadLogAsync(logId);
        entry.Result.Should().Be(NotificationResult.Failed);
        entry.Error.Should().Contain("Gave up after 3 retries");

        bool retryExists = await Factory.QueryDbAsync(database =>
            database.PushRetries.AnyAsync(row => row.NotificationLogId == logId));

        retryExists.Should().BeFalse();
    }

    [Fact]
    public async Task GivenARetryThatSucceeds_WhenTheJobRuns_ThenTheRowBecomesSent()
    {
        Recipient recipient = await SeedAsync();
        _sender.Script(
            recipient.Endpoint,
            PushSendResult.Failed("Push service returned 503.", 503),
            PushSendResult.Accepted(201));

        await SendAsync(recipient, NotificationType.FridayReminder, week: 11, Payload());
        Guid logId = await SingleLogIdAsync(recipient, NotificationType.FridayReminder, 11);

        PushRetry retry = await SingleRetryAsync(logId);
        await RunRetryAsync(retry.Id);

        NotificationLogEntry entry = await ReadLogAsync(logId);
        entry.Result.Should().Be(NotificationResult.Sent);
        entry.Error.Should().BeNull();
        entry.SubscriptionId.Should().Be(recipient.SubscriptionId);

        bool retryExists = await Factory.QueryDbAsync(database =>
            database.PushRetries.AnyAsync(row => row.NotificationLogId == logId));

        retryExists.Should().BeFalse();
    }

    [Fact]
    public async Task GivenARetryWhoseSubscriptionHasExpired_WhenTheJobRuns_ThenItIsDeletedAndLoggedExpired()
    {
        Recipient recipient = await SeedAsync();
        _sender.Script(
            recipient.Endpoint,
            PushSendResult.Failed("Push service returned 500.", 500),
            PushSendResult.Gone("Push service returned 404.", 404));

        await SendAsync(recipient, NotificationType.SaturdayReminder, week: 12, Payload());
        Guid logId = await SingleLogIdAsync(recipient, NotificationType.SaturdayReminder, 12);

        PushRetry retry = await SingleRetryAsync(logId);
        await RunRetryAsync(retry.Id);

        NotificationLogEntry entry = await ReadLogAsync(logId);
        entry.Result.Should().Be(NotificationResult.Expired);

        bool subscriptionExists = await Factory.QueryDbAsync(database =>
            database.PushSubscriptions.AnyAsync(subscription => subscription.Id == recipient.SubscriptionId));

        subscriptionExists.Should().BeFalse();
    }

    [Fact]
    public async Task GivenAReminderAlreadySentThisWeek_WhenSendingAgain_ThenItIsSkippedAndNothingGoesOut()
    {
        Recipient recipient = await SeedAsync();

        NotificationResult first = await SendAsync(recipient, NotificationType.FridayReminder, week: 13, Payload());
        NotificationResult second = await SendAsync(recipient, NotificationType.FridayReminder, week: 13, Payload());

        first.Should().Be(NotificationResult.Sent);
        second.Should().Be(NotificationResult.Skipped, "the filtered unique index is the once-per-week guarantee");

        _sender.SentTo(recipient.Endpoint).Should().ContainSingle();

        int rows = await Factory.QueryDbAsync(database => database.NotificationLog.CountAsync(entry =>
            entry.UserId == recipient.UserId
            && entry.LeagueId == recipient.LeagueId
            && entry.Week == 13
            && entry.Type == NotificationType.FridayReminder));

        rows.Should().Be(1);
    }

    [Fact]
    public async Task GivenAnEventNotificationTwice_WhenSending_ThenBothGoOut()
    {
        Recipient recipient = await SeedAsync();

        NotificationResult first = await SendAsync(recipient, NotificationType.GamesAdded, week: 14, Payload());
        NotificationResult second = await SendAsync(recipient, NotificationType.GamesAdded, week: 14, Payload());

        first.Should().Be(NotificationResult.Sent);
        second.Should().Be(NotificationResult.Sent, "only the three reminder types are once per week");

        _sender.SentTo(recipient.Endpoint).Should().HaveCount(2);
    }

    [Fact]
    public async Task GivenAMemberWithNoDevice_WhenSending_ThenItIsSkipped()
    {
        Recipient recipient = await SeedAsync(subscribe: false);

        NotificationResult result = await SendAsync(recipient, NotificationType.FridayReminder, week: 1, Payload());

        result.Should().Be(NotificationResult.Skipped);

        NotificationLogEntry entry = await SingleLogAsync(recipient, NotificationType.FridayReminder, week: 1);
        entry.Error.Should().Contain("no push subscription");
        _sender.Sent.Should().NotContain(push => push.Endpoint == recipient.Endpoint);
    }

    [Fact]
    public async Task GivenTwoDevicesWhereOneIsGone_WhenSending_ThenTheRowIsSentAndOnlyTheDeadOneIsDeleted()
    {
        Recipient recipient = await SeedAsync();
        Guid secondSubscriptionId = Guid.CreateVersion7();
        string secondEndpoint = $"https://push.example/send/{Guid.CreateVersion7():N}";

        await Factory.ExecuteDbAsync(async database =>
        {
            database.PushSubscriptions.Add(new PushSubscription
            {
                Id = secondSubscriptionId,
                UserId = recipient.UserId,
                Endpoint = secondEndpoint,
                P256dh = "k",
                Auth = "a",
                CreatedUtc = DateTime.UtcNow,
            });

            await database.SaveChangesAsync();
        });

        _sender.Script(secondEndpoint, PushSendResult.Gone("Push service returned 410.", 410));

        NotificationResult result = await SendAsync(recipient, NotificationType.FridayReminder, week: 2, Payload());

        result.Should().Be(NotificationResult.Sent, "one live device is enough for the notification to count");

        var remaining = await Factory.QueryDbAsync(database => database.PushSubscriptions
            .Where(subscription => subscription.UserId == recipient.UserId)
            .Select(subscription => subscription.Id)
            .ToListAsync());

        remaining.Should().BeEquivalentTo([recipient.SubscriptionId]);
    }

    [Fact]
    public async Task GivenNoVapidKeys_WhenSending_ThenItReportsFailedWithoutThrowing()
    {
        // The shared factory has no Push__* configuration, so the real NullPushSender is in play.
        Recipient recipient = await SeedAsync(factory: _fixture.Factory);

        await using AsyncServiceScope scope = _fixture.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        NotificationResult result = await notifications.SendToUserAsync(
            recipient.UserId,
            NotificationType.FridayReminder,
            recipient.LeagueId,
            week: 3,
            Payload(),
            ttl: null,
            CancellationToken.None);

        result.Should().Be(NotificationResult.Failed);

        NotificationLogEntry entry = await _fixture.Factory.QueryDbAsync(database => database.NotificationLog
            .SingleAsync(row => row.UserId == recipient.UserId
                && row.Week == 3
                && row.Type == NotificationType.FridayReminder));

        entry.Result.Should().Be(NotificationResult.Failed);
        entry.Error.Should().Contain("Push:Vapid");
    }

    private static PushPayload Payload() =>
        new("Title", "Body", "/leagues/x/picks", "tag");

    private async Task<Recipient> SeedAsync(bool subscribe = true, ApiFactory? factory = null)
    {
        ApiFactory target = factory ?? Factory;

        return await target.QueryDbAsync(async database =>
        {
            User user = await TestUsers.CreateUserAsync(database);
            Domain.Leagues.League league = await TestUsers.CreateLeagueAsync(database, user);
            await TestUsers.CreateMembershipAsync(database, league, user, MembershipRole.Commissioner);

            Guid subscriptionId = Guid.CreateVersion7();
            string endpoint = $"https://push.example/send/{Guid.CreateVersion7():N}";

            if (subscribe)
            {
                database.PushSubscriptions.Add(new PushSubscription
                {
                    Id = subscriptionId,
                    UserId = user.Id,
                    Endpoint = endpoint,
                    P256dh = "p256dh",
                    Auth = "auth",
                    UserAgent = "Test device",
                    CreatedUtc = DateTime.UtcNow,
                });

                await database.SaveChangesAsync();
            }

            return new Recipient(user.Id, league.Id, subscriptionId, endpoint);
        });
    }

    private async Task<NotificationResult> SendAsync(
        Recipient recipient,
        NotificationType type,
        int week,
        PushPayload payload)
    {
        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        return await notifications.SendToUserAsync(
            recipient.UserId,
            type,
            recipient.LeagueId,
            week,
            payload,
            ttl: null,
            CancellationToken.None);
    }

    private async Task RunRetryAsync(Guid retryId) =>
        await WithJobAsync(async job =>
        {
            await job.RunAsync(
                new OneShotOccurrence(retryId.ToString("N"), DateTimeOffset.UtcNow.AddHours(1)),
                CancellationToken.None);

            return true;
        });

    private async Task<TResult> WithJobAsync<TResult>(Func<PushRetryJob, Task<TResult>> work)
    {
        await using AsyncServiceScope scope = Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        PushRetryJob job = scope.ServiceProvider.GetServices<IOneShotJob>().OfType<PushRetryJob>().Single();
        return await work(job);
    }

    private Task<NotificationLogEntry> ReadLogAsync(Guid logId) =>
        Factory.QueryDbAsync(database => database.NotificationLog.AsNoTracking().SingleAsync(entry => entry.Id == logId));

    private Task<NotificationLogEntry> SingleLogAsync(Recipient recipient, NotificationType type, int week) =>
        Factory.QueryDbAsync(database => database.NotificationLog.AsNoTracking().SingleAsync(entry =>
            entry.UserId == recipient.UserId
            && entry.LeagueId == recipient.LeagueId
            && entry.Week == week
            && entry.Type == type));

    private async Task<Guid> SingleLogIdAsync(Recipient recipient, NotificationType type, int week) =>
        (await SingleLogAsync(recipient, type, week)).Id;

    private Task<PushRetry> SingleRetryAsync(Guid logId) =>
        Factory.QueryDbAsync(database => database.PushRetries.AsNoTracking()
            .SingleAsync(retry => retry.NotificationLogId == logId));

    private Task<PushSubscription> ReadSubscriptionAsync(Guid subscriptionId) =>
        Factory.QueryDbAsync(database => database.PushSubscriptions.AsNoTracking()
            .SingleAsync(subscription => subscription.Id == subscriptionId));

    private sealed record Recipient(Guid UserId, Guid LeagueId, Guid SubscriptionId, string Endpoint);
}
