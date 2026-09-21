using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Shared.Contracts.Dev;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>/api/admin/fixture/clock</c> (P10-01): the dev clock boots where <c>Clock:NowUtc</c> says,
/// moves and freezes on <c>PUT</c>, and comes back to real time on <c>DELETE</c>.
/// </summary>
/// <remarks>
/// Boots its own host over the shared database so its clock is the <c>DevTimeProvider</c>
/// <c>AddInfrastructure</c> registers in Testing; the fixture's <c>PinnedFactory</c> swaps in a
/// fixed clock instead, which is the 409 case.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class DevClockEndpointTests : IAsyncLifetime
{
    private static readonly DateTimeOffset ConfiguredNow = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly ApiTestFixture _fixture;
    private ApiFactory? _app;
    private Guid _userId;

    public DevClockEndpointTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _app = new ApiFactory(
            _fixture.Database.ConnectionString,
            settings: new Dictionary<string, string> { ["Clock:NowUtc"] = ConfiguredNow.ToString("o") });

        _userId = (await _app.QueryDbAsync(database => TestUsers.CreateUserAsync(database))).Id;
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task GivenClockNowUtcConfigured_WhenBooted_ThenTheAppClockStartsThereAndRunsOn()
    {
        using HttpClient client = App.CreateClientAs(_userId);

        DevClockResponse clock = await ReadAsync(await client.GetAsync("/api/admin/fixture/clock"));

        clock.NowUtc.Should().BeCloseTo(ConfiguredNow, TimeSpan.FromMinutes(5), "the clock started at the configured instant and has run since boot");
        clock.RealNowUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        clock.IsShifted.Should().BeTrue();
        clock.IsFrozen.Should().BeFalse();
        clock.CurrentWeek.Should().Be(ApiTestFixture.PinnedCurrentWeek);
        clock.SeasonState.Should().Be("InSeason");
        clock.NowEasternDisplay.Should().EndWith(" ET");
    }

    [Fact]
    public async Task GivenABodyWithNothingSet_WhenPut_ThenItIs400()
    {
        using HttpClient client = App.CreateMutatingClientAs(_userId);

        using HttpResponseMessage response = await client.PutAsJsonAsync("/api/admin/fixture/clock", new DevClockRequest());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAnAdvance_WhenPut_ThenTheClockMovesByThatMuch()
    {
        using HttpClient client = App.CreateMutatingClientAs(_userId);
        DevClockResponse before = await ReadAsync(await client.GetAsync("/api/admin/fixture/clock"));

        DevClockResponse after = await ReadAsync(
            await client.PutAsJsonAsync("/api/admin/fixture/clock", new DevClockRequest(Advance: TimeSpan.FromDays(3))));

        (after.NowUtc - before.NowUtc).Should().BeCloseTo(TimeSpan.FromDays(3), TimeSpan.FromMinutes(1));
        after.CurrentWeek.Should().Be(ApiTestFixture.PinnedCurrentWeek, "Wednesday plus three days is still the same Sunday-to-Saturday week");
    }

    [Fact]
    public async Task GivenNowUtcAndFrozen_WhenPut_ThenTheClockStandsStillThereUntilThawed()
    {
        using HttpClient client = App.CreateMutatingClientAs(_userId);
        var saturdayNoon = new DateTimeOffset(2026, 10, 17, 16, 0, 0, TimeSpan.Zero);

        DevClockResponse frozen = await ReadAsync(
            await client.PutAsJsonAsync("/api/admin/fixture/clock", new DevClockRequest(NowUtc: saturdayNoon, Frozen: true)));
        DevClockResponse again = await ReadAsync(await client.GetAsync("/api/admin/fixture/clock"));
        DevClockResponse thawed = await ReadAsync(
            await client.PutAsJsonAsync("/api/admin/fixture/clock", new DevClockRequest(Frozen: false)));

        frozen.IsFrozen.Should().BeTrue();
        frozen.NowUtc.Should().Be(saturdayNoon);
        again.NowUtc.Should().Be(saturdayNoon, "a frozen clock reads the same instant on every request");
        frozen.NowEasternDisplay.Should().Be("Sat 12:00 PM ET");
        thawed.IsFrozen.Should().BeFalse();
        thawed.NowUtc.Should().BeCloseTo(saturdayNoon, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GivenAShiftedClock_WhenDeleted_ThenItReadsRealTimeAgain()
    {
        using HttpClient client = App.CreateMutatingClientAs(_userId);
        await client.PutAsJsonAsync("/api/admin/fixture/clock", new DevClockRequest(Advance: TimeSpan.FromDays(30), Frozen: true));

        DevClockResponse reset = await ReadAsync(await client.DeleteAsync("/api/admin/fixture/clock"));

        reset.IsShifted.Should().BeFalse();
        reset.IsFrozen.Should().BeFalse();
        reset.NowUtc.Should().BeCloseTo(reset.RealNowUtc, TimeSpan.FromSeconds(5));
        reset.Offset.Should().BeCloseTo(TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GivenAHostWithAPinnedTestClock_WhenRead_ThenItIs409()
    {
        // PinnedFactory replaces TimeProvider with the tests' own FixedTimeProvider, so there is
        // no DevTimeProvider to move; the route says so instead of pretending.
        using HttpClient client = _fixture.PinnedFactory.CreateClientAs(_userId);

        using HttpResponseMessage response = await client.GetAsync("/api/admin/fixture/clock");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenAnAnonymousCaller_WhenReadingOrMovingTheClock_ThenItIs401()
    {
        using HttpClient client = App.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "NcaafPickEm");

        using HttpResponseMessage read = await client.GetAsync("/api/admin/fixture/clock");
        using HttpResponseMessage move = await client.PutAsJsonAsync(
            "/api/admin/fixture/clock", new DevClockRequest(Advance: TimeSpan.FromHours(1)));

        read.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        move.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private ApiFactory App => _app ?? throw new InvalidOperationException("InitializeAsync has not run.");

    private static async Task<DevClockResponse> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            string body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
            return JsonSerializer.Deserialize<DevClockResponse>(body, JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException("Empty clock body.");
        }
    }
}
