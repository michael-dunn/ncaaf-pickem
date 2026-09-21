using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Shared.Contracts.Dev;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>/api/admin/fixture/demo</c> (P10-01): walks the seeded demo league through the fixture week
/// the way the Dev tools page does - move the clock in, generate, fill picks, lock, poll the
/// all-Final snapshot, reset - and checks each step left the week where a member would see it.
/// </summary>
/// <remarks>
/// Runs on its own throwaway database: reset puts the fixture games back to Scheduled and the
/// steps lock and score a week, none of which the shared fixture database should inherit.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class DemoWeekEndpointTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixtureWednesday = new(2026, 10, 14, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RealSeasonWeek4 = new(2026, 9, 23, 16, 0, 0, TimeSpan.Zero);

    private SqlTestDatabase? _database;
    private ApiFactory? _app;
    private Guid _michaelUserId;

    public async Task InitializeAsync()
    {
        _database = await SqlTestDatabase.CreateAsync();
        _app = new ApiFactory(
            _database.ConnectionString,
            settings: new Dictionary<string, string> { ["Seed:DemoLeague"] = "true" });

        // Booting the host runs FixtureSeederHostedService; the first request waits for it.
        using HttpClient warmup = _app.CreateClient();
        using HttpResponseMessage ready = await warmup.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);

        _michaelUserId = await _app.QueryDbAsync(database => database.Users
            .Where(user => user.ExternalSubject == "fixture:michael")
            .Select(user => user.Id)
            .SingleAsync());
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [Fact]
    public async Task GivenTheSeededDemoLeague_WhenRead_ThenItHasFiveMembersAndADefaultRule()
    {
        using HttpClient client = App.CreateClientAs(_michaelUserId);

        DemoWeekResponse demo = await ReadAsync(await client.GetAsync("/api/admin/fixture/demo"));

        demo.LeagueName.Should().Be("Family League");
        demo.Members.Should().HaveCount(5);
        demo.Members.Should().ContainSingle(member => member.Role == "Commissioner" && member.Name == "Michael");
        demo.Members.Should().OnlyContain(member => member.Status == "NotStarted" && member.PickedCount == 0 && member.Points == null);
        demo.Notes.Should().BeEmpty();

        int defaultRules = await App.QueryDbAsync(database => database.GameSetRules
            .CountAsync(rule => rule.LeagueId == demo.LeagueId && rule.Week == null));
        defaultRules.Should().Be(1, "FixtureSeeder gives the demo league a Top 25 default rule so the week can generate on its own");
    }

    [Fact]
    public async Task GivenTheClockOutsideTheFixtureWeek_WhenGenerating_ThenNothingIsCreatedAndTheNoteSaysWhy()
    {
        using HttpClient client = App.CreateMutatingClientAs(_michaelUserId);
        await SetClockAsync(client, RealSeasonWeek4);

        DemoWeekResponse demo = await ReadAsync(await client.PostAsync("/api/admin/fixture/demo/generate", null));

        demo.CurrentWeek.Should().Be(4, "September 23, 2026 is in week 4 of the fixture calendar (week 1 ends Saturday September 5)");
        demo.HasFixtureGames.Should().BeFalse();
        demo.Set.Should().BeNull();
        demo.Notes.Should().ContainSingle().Which.Should().Contain("no games in week 4").And.Contain("week 7");
    }

    [Fact]
    public async Task GivenTheFixtureWeek_WhenSteppingGeneratePicksLockAndPoll_ThenTheWeekEndsScoredAndResetClearsIt()
    {
        using HttpClient client = App.CreateMutatingClientAs(_michaelUserId);
        await ReadAsync(await client.PostAsync("/api/admin/fixture/demo/reset", null));
        await SetClockAsync(client, FixtureWednesday);

        // 1. Generate: the Top 25 rule over the fixture week gives a real set with a lock instant.
        DemoWeekResponse generated = await ReadAsync(await client.PostAsync("/api/admin/fixture/demo/generate", null));
        generated.CurrentWeek.Should().Be(FixtureReferenceDataProvider.FixtureWeek);
        generated.HasFixtureGames.Should().BeTrue();
        generated.Set.Should().NotBeNull();
        generated.Set!.ActiveGameCount.Should().BeGreaterThan(0);
        generated.Set.LockAtUtc.Should().NotBeNull();
        generated.Set.IsFrozenNow.Should().BeFalse();
        generated.Set.LockedUtc.Should().BeNull();
        generated.Notes.Should().Contain(note => note.StartsWith("generate: ") && note.Contains("game(s)"));
        int activeGames = generated.Set.ActiveGameCount;

        // 2. Fill the other members' picks; the caller (Michael) is left alone by default.
        DemoWeekResponse others = await ReadAsync(await client.PostAsync("/api/admin/fixture/demo/picks", null));
        others.Notes.Should().Contain(note => note.Contains("Michael skipped"));
        others.Members.Single(member => member.Name == "Michael").Should()
            .Match<DemoMemberDto>(member => member.Status == "NotStarted" && member.PickedCount == 0);
        others.Members.Where(member => member.Name != "Michael").Should()
            .OnlyContain(member => member.Status == "Submitted" && member.PickedCount == activeGames);

        // 2b. includeMe fills the caller too.
        DemoWeekResponse everyone = await ReadAsync(await client.PostAsync("/api/admin/fixture/demo/picks?includeMe=true", null));
        everyone.Members.Should().OnlyContain(member => member.Status == "Submitted" && member.PickedCount == activeGames);

        // 3. Lock now, whatever the clock says.
        DemoWeekResponse locked = await ReadAsync(await client.PostAsync("/api/admin/fixture/demo/lock", null));
        locked.Set!.LockedUtc.Should().NotBeNull();
        locked.Set.IsFrozenNow.Should().BeTrue();
        locked.Members.Should().OnlyContain(member => member.Status == "Locked");

        // A second lock is idempotent, and picks are now refused with the real Locked rule.
        DemoWeekResponse lockedAgain = await ReadAsync(await client.PostAsync("/api/admin/fixture/demo/lock", null));
        lockedAgain.Notes.Should().ContainSingle().Which.Should().StartWith("lock: already locked");
        DemoWeekResponse refused = await ReadAsync(await client.PostAsync("/api/admin/fixture/demo/picks?includeMe=true", null));
        refused.Notes.Should().Contain(note => note.Contains("Locked"));

        // 4. Poll the all-Final snapshot: scores land, finals raise GameWentFinal, the week is scored.
        DemoWeekResponse polled = await ReadAsync(await client.PostAsync("/api/admin/fixture/demo/poll?snapshot=6", null));
        polled.Snapshot.Should().Be(6);
        polled.Notes.Should().ContainSingle().Which.Should().StartWith("poll: snapshot 6").And.Contain("matched");
        polled.Set!.FinalCount.Should().Be(activeGames, "snapshot 6 is every fixture game Final");
        polled.Set.IsComplete.Should().BeTrue();
        polled.Members.Should().OnlyContain(member => member.Points != null && member.CorrectCount != null);
        polled.Members.Select(member => member.Points!.Value).Should().Contain(points => points > 0);

        // 5. Reset: the week is gone, the fixture games are Scheduled again, the snapshot is back at 1.
        DemoWeekResponse reset = await ReadAsync(await client.PostAsync("/api/admin/fixture/demo/reset", null));
        reset.Set.Should().BeNull();
        reset.Snapshot.Should().Be(1);
        reset.Members.Should().OnlyContain(member => member.Status == "NotStarted" && member.PickedCount == 0 && member.Points == null);
        reset.Notes.Should().ContainSingle().Which.Should().StartWith("reset: deleted 1 set(s)");

        int finalGames = await App.QueryDbAsync(database => database.Games
            .CountAsync(game => game.SeasonYear == FixtureReferenceDataProvider.FixtureSeason
                && game.Week == FixtureReferenceDataProvider.FixtureWeek
                && game.HomeScore != null));
        finalGames.Should().Be(0, "reset puts every fixture game back to Scheduled with no score");
    }

    [Fact]
    public async Task GivenAnOutOfRangeSnapshot_WhenPolling_ThenNothingIsPolledAndTheNoteSaysSo()
    {
        using HttpClient client = App.CreateMutatingClientAs(_michaelUserId);

        DemoWeekResponse demo = await ReadAsync(await client.PostAsync("/api/admin/fixture/demo/poll?snapshot=9", null));

        demo.Notes.Should().ContainSingle().Which.Should().Contain("between 1 and 6");
    }

    [Fact]
    public async Task GivenAnAnonymousCaller_WhenReadingTheDemoWeek_ThenItIs401()
    {
        using HttpClient client = App.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/admin/fixture/demo");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private ApiFactory App => _app ?? throw new InvalidOperationException("InitializeAsync has not run.");

    private static async Task SetClockAsync(HttpClient client, DateTimeOffset nowUtc)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/admin/fixture/clock", new DevClockRequest(NowUtc: nowUtc));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<DemoWeekResponse> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            string body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
            return JsonSerializer.Deserialize<DemoWeekResponse>(body, JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException("Empty demo body.");
        }
    }
}
