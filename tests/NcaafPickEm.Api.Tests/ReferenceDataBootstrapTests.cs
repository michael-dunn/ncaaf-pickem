using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Jobs.Refresh;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P8-06: <see cref="ReferenceDataBootstrapHostedService"/> — the first start on an empty
/// database fetches the season calendar and the current week's data itself, instead of leaving
/// the app unusable until the Tuesday refresh jobs run (D-164).
/// </summary>
/// <remarks>
/// Each test boots its own host against its own <see cref="SqlTestDatabase"/> with
/// <c>Providers:ReferenceData=Cfbd</c> — the only configuration in which the bootstrap runs, and
/// the one that also puts <c>DbSeasonWeekSource</c> in charge of the calendar — and then replaces
/// the CFBD provider with <see cref="BootstrapProvider"/>, which serves the Week 7 2026 fixture
/// payloads and counts every call. Nothing here ever reaches the network.
/// </remarks>
public sealed class ReferenceDataBootstrapTests : IAsyncLifetime
{
    /// <summary>The fixture season, and the season <see cref="NowUtc"/> falls in.</summary>
    private const int Season = FixtureReferenceDataProvider.FixtureSeason;

    /// <summary>Week 7 of that season, the only week the fixture payloads describe.</summary>
    private const int Week = FixtureReferenceDataProvider.FixtureWeek;

    /// <summary>Wednesday 2026-10-14, inside the Week 7 window (Sun 10-11 to Sat 10-17 ET).</summary>
    private static readonly DateTimeOffset NowUtc = ApiTestFixture.PinnedNowUtc;

    private SqlTestDatabase _database = null!;

    /// <inheritdoc />
    public async Task InitializeAsync() => _database = await SqlTestDatabase.CreateAsync();

    /// <inheritdoc />
    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenAnEmptyDatabase_WhenTheHostStarts_ThenTheSeasonAndCurrentWeekAreIngested()
    {
        var provider = new BootstrapProvider();
        await using ApiFactory factory = CreateFactory(provider);

        ReferenceDataBootstrapResult result = await RunBootstrapAsync(factory);

        result.Ran.Should().BeTrue();
        result.Season.Should().Be(Season);
        result.CurrentWeek.Should().Be(Week);
        result.Failures.Should().Be(0);

        // Calendar, conferences + teams, this week's and next week's schedule, rankings, lines.
        result.ProviderCalls.Should().BeInRange(6, 8);
        provider.Calls.Should().Be(result.ProviderCalls);

        await factory.ExecuteDbAsync(async database =>
        {
            (await database.SeasonWeeks.CountAsync(week => week.SeasonYear == Season)).Should().BeGreaterThan(0);
            (await database.Teams.CountAsync()).Should().BeGreaterThan(0);
            (await database.Games.CountAsync(game => game.SeasonYear == Season && game.Week == Week))
                .Should().BeGreaterThan(0);
            (await database.Rankings.CountAsync(rank => rank.SeasonYear == Season && rank.Week == Week))
                .Should().BeGreaterThan(0);
            (await database.GameLines.CountAsync()).Should().BeGreaterThan(0);

            foreach (RefreshDataType dataType in
                new[] { RefreshDataType.Teams, RefreshDataType.Schedule, RefreshDataType.Rankings, RefreshDataType.Lines })
            {
                DataRefreshStatus status = await database.DataRefreshStatuses
                    .SingleAsync(row => row.DataType == dataType);
                status.LastSuccessUtc.Should().NotBeNull($"{dataType} was bootstrapped");
                status.LastError.Should().BeNull();
            }
        });
    }

    [Fact]
    public async Task GivenTheDataIsAlreadyOnFile_WhenTheHostStartsAgain_ThenItMakesNoProviderCalls()
    {
        var first = new BootstrapProvider();
        await using (ApiFactory factory = CreateFactory(first))
        {
            (await RunBootstrapAsync(factory)).Ran.Should().BeTrue();
        }

        // A second start against the same database: the calendar and teams are there, so the
        // bootstrap must not spend another handful of the free tier's monthly calls.
        var second = new BootstrapProvider();
        await using ApiFactory restarted = CreateFactory(second);

        ReferenceDataBootstrapResult result = await RunBootstrapAsync(restarted);

        result.Ran.Should().BeFalse();
        result.ProviderCalls.Should().Be(0);
        second.Calls.Should().Be(0);
    }

    [Fact]
    public async Task GivenTheProviderFails_WhenTheHostStarts_ThenItStaysHealthyAndTheStatusShowsTheError()
    {
        var provider = new BootstrapProvider { Failure = "simulated CFBD outage" };
        await using ApiFactory factory = CreateFactory(provider);

        ReferenceDataBootstrapResult result = await RunBootstrapAsync(factory);

        result.Ran.Should().BeTrue();
        result.Failures.Should().BeGreaterThan(0);

        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);

        await factory.ExecuteDbAsync(async database =>
        {
            (await database.SeasonWeeks.AnyAsync()).Should().BeFalse();

            DataRefreshStatus schedule = await database.DataRefreshStatuses
                .SingleAsync(row => row.DataType == RefreshDataType.Schedule);
            schedule.LastError.Should().Contain("simulated CFBD outage");
            schedule.LastSuccessUtc.Should().BeNull();

            DataRefreshStatus teams = await database.DataRefreshStatuses
                .SingleAsync(row => row.DataType == RefreshDataType.Teams);
            teams.LastError.Should().Contain("simulated CFBD outage");
        });
    }

    [Fact]
    public async Task GivenFixtureMode_WhenTheHostStarts_ThenTheBootstrapNeverRuns()
    {
        var provider = new BootstrapProvider();

        // No settings override at all: ApiFactory's own defaults are Fixture with jobs off,
        // which is every other test in this suite.
        await using var factory = new ApiFactory(
            _database.ConnectionString,
            timeProvider: new FixedTimeProvider(NowUtc),
            configureServices: services =>
            {
                services.RemoveAll<IReferenceDataProvider>();
                services.AddSingleton<IReferenceDataProvider>(provider);
            });

        ReferenceDataBootstrapHostedService bootstrap = Resolve(factory);
        await bootstrap.Completed.WaitAsync(TimeSpan.FromMinutes(1));

        bootstrap.Result.Should().BeNull("the bootstrap is off outside Cfbd mode");

        // The fixture seeder owns this database in Fixture mode and calls the provider itself,
        // so "no provider calls" is not the tell; "no ingest ever ran" is - every ingest writes
        // a DataRefreshStatus row and nothing else does.
        provider.Calls.Should().BeGreaterThan(0, "the fixture seeder read the fixture payloads");
        (await factory.QueryDbAsync(database => database.DataRefreshStatuses.AnyAsync()))
            .Should().BeFalse();
    }

    private ApiFactory CreateFactory(BootstrapProvider provider) => new(
        _database.ConnectionString,
        timeProvider: new FixedTimeProvider(NowUtc),
        configureServices: services =>
        {
            services.RemoveAll<IReferenceDataProvider>();
            services.AddSingleton<IReferenceDataProvider>(provider);
        },
        settings: new Dictionary<string, string>
        {
            ["Providers:ReferenceData"] = "Cfbd",

            // Jobs stay off (nothing here may run a cron tick), so the bootstrap is turned on
            // explicitly - the key a deployment would never need to set.
            [ReferenceDataBootstrapHostedService.EnabledKey] = "true",
        });

    private static async Task<ReferenceDataBootstrapResult> RunBootstrapAsync(ApiFactory factory)
    {
        ReferenceDataBootstrapHostedService bootstrap = Resolve(factory);
        await bootstrap.Completed.WaitAsync(TimeSpan.FromMinutes(1));

        bootstrap.Result.Should().NotBeNull("the bootstrap was enabled for this host");
        return bootstrap.Result!;
    }

    /// <summary>
    /// The host's own instance of the background service — <c>AddHostedService</c> registers it
    /// as a singleton <c>IHostedService</c>, so this is the one that ran, not a new one.
    /// </summary>
    private static ReferenceDataBootstrapHostedService Resolve(ApiFactory factory) =>
        factory.Services.GetServices<IHostedService>().OfType<ReferenceDataBootstrapHostedService>().Single();

    /// <summary>
    /// Stands in for <c>CfbdReferenceDataProvider</c>: the Week 7 2026 fixture payloads, plus the
    /// CFBD-shaped season calendar the fixture data set does not carry, with every call counted
    /// and an optional simulated outage.
    /// </summary>
    private sealed class BootstrapProvider : IReferenceDataProvider
    {
        /// <summary>Saturday of Week 1 of the 2026 season, as <c>FixtureSeasonWeekSource</c> has it.</summary>
        private static readonly DateOnly WeekOneSaturday = new(2026, 9, 5);

        private const int ChampionshipWeek = 15;

        private readonly FixtureReferenceDataProvider _fixtures = new();
        private int _calls;

        /// <summary>Set to make every call throw, the way a CFBD outage would.</summary>
        public string? Failure { get; init; }

        /// <summary>How many provider methods have been called.</summary>
        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyList<ProviderConference>> GetConferencesAsync(
            int season, CancellationToken cancellationToken = default) =>
            Track(() => _fixtures.GetConferencesAsync(season, cancellationToken));

        public Task<IReadOnlyList<ProviderTeam>> GetTeamsAsync(
            int season, CancellationToken cancellationToken = default) =>
            Track(() => _fixtures.GetTeamsAsync(season, cancellationToken));

        public Task<IReadOnlyList<ProviderGame>> GetGamesAsync(
            int season, int week, CancellationToken cancellationToken = default) =>
            Track(async () =>
            {
                IReadOnlyList<ProviderGame> games = await _fixtures.GetGamesAsync(season, Week, cancellationToken);

                // The bootstrap also fetches next week, which the fixture data set has no games
                // for; CFBD would have some, so the same slate is re-dated rather than empty
                // (an empty week is a refusal, D-056).
                return week == Week
                    ? games
                    : [.. games.Select(game => game with
                    {
                        Week = week,
                        CfbdGameId = game.CfbdGameId + (week * 1000),
                        KickoffUtc = game.KickoffUtc.AddDays(7 * (week - Week)),
                    })];
            });

        public Task<IReadOnlyList<ProviderRanking>> GetRankingsAsync(
            int season, int week, CancellationToken cancellationToken = default) =>
            Track(() => _fixtures.GetRankingsAsync(season, week, cancellationToken));

        public Task<IReadOnlyList<ProviderLine>> GetLinesAsync(
            int season, int week, CancellationToken cancellationToken = default) =>
            Track(() => _fixtures.GetLinesAsync(season, week, cancellationToken));

        /// <summary>
        /// CFBD's own calendar shape — a Monday 03:00 ET through next-Monday 02:59 ET window per
        /// week — which <c>CfbdCalendarNormalization</c> turns into the Sunday-Saturday
        /// <c>SeasonWeek</c> rows the app stores.
        /// </summary>
        public Task<IReadOnlyList<ProviderCalendarWeek>> GetCalendarAsync(
            int season, CancellationToken cancellationToken = default) =>
            Track(() =>
            {
                var weeks = new List<ProviderCalendarWeek>(ChampionshipWeek);

                for (int week = 1; week <= ChampionshipWeek; week++)
                {
                    DateOnly saturday = WeekOneSaturday.AddDays(7 * (week - 1));
                    DateTime start = SeasonCalendar
                        .ToUtc(saturday.AddDays(-5).ToDateTime(new TimeOnly(3, 0))).UtcDateTime;
                    DateTime end = SeasonCalendar
                        .ToUtc(saturday.AddDays(2).ToDateTime(new TimeOnly(2, 59))).UtcDateTime;

                    weeks.Add(new ProviderCalendarWeek(
                        season,
                        week,
                        week == ChampionshipWeek ? "postseason" : "regular",
                        start,
                        end));
                }

                return Task.FromResult<IReadOnlyList<ProviderCalendarWeek>>(weeks);
            });

        private Task<T> Track<T>(Func<Task<T>> call)
        {
            Interlocked.Increment(ref _calls);

            return Failure is string failure
                ? Task.FromException<T>(new InvalidOperationException(failure))
                : call();
        }
    }
}
