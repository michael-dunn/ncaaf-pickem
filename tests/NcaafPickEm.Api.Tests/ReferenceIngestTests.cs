using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <see cref="ReferenceDataIngestService"/> (P2-02), driven by the same
/// <see cref="FixtureReferenceDataProvider"/> the app boots with — the card's "using the fixture
/// payloads" — plus small decorator wrappers below that simulate a dropped game and a failing
/// provider call, which the real CFBD provider cannot be made to do deterministically in a test.
/// </summary>
/// <remarks>
/// Deliberately does <em>not</em> use the shared <see cref="ApiTestFixture"/> database: every
/// test here writes <c>DataRefreshStatus</c> rows with non-null <c>LastSuccessUtc</c>, which
/// would break <c>AdminDataStatusTests</c>' "every slice reports nulls" assertion (a placeholder
/// for "P2-02 has not run yet") if the two shared state. Each test gets its own throwaway,
/// migrated database via <see cref="SqlTestDatabase"/> instead — slower, but independent of test
/// order and of every other test class.
/// </remarks>
public sealed class ReferenceIngestTests : IAsyncLifetime
{
    private const int Season = FixtureReferenceDataProvider.FixtureSeason;
    private const int Week = FixtureReferenceDataProvider.FixtureWeek;

    private SqlTestDatabase _database = null!;

    /// <inheritdoc />
    public async Task InitializeAsync() => _database = await SqlTestDatabase.CreateAsync();

    /// <inheritdoc />
    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenTeamsIngestedTwice_ThenNoDuplicateConferencesTeamsOrAliases()
    {
        await using AppDbContext database = _database.CreateContext();
        var service = CreateService(database, new FixtureReferenceDataProvider());

        TeamsIngestResult first = await service.IngestTeamsAsync(Season);
        first.Success.Should().BeTrue();

        int conferencesAfterFirst = await database.Conferences.CountAsync();
        int teamsAfterFirst = await database.Teams.CountAsync();
        int aliasesAfterFirst = await database.TeamAliases.CountAsync(a => a.Source == ProviderSource.Cfbd);

        TeamsIngestResult second = await service.IngestTeamsAsync(Season);
        second.Success.Should().BeTrue();

        (await database.Conferences.CountAsync()).Should().Be(conferencesAfterFirst);
        (await database.Teams.CountAsync()).Should().Be(teamsAfterFirst);
        (await database.TeamAliases.CountAsync(a => a.Source == ProviderSource.Cfbd)).Should().Be(aliasesAfterFirst);

        DataRefreshStatus status = await database.DataRefreshStatuses.SingleAsync(s => s.DataType == RefreshDataType.Teams);
        status.LastSuccessUtc.Should().NotBeNull();
        status.LastError.Should().BeNull();
    }

    [Fact]
    public async Task GivenFcsOpponents_WhenTeamsIngested_ThenClassificationStoredAsFcs()
    {
        await using AppDbContext database = _database.CreateContext();
        var service = CreateService(database, new FixtureReferenceDataProvider());

        await service.IngestTeamsAsync(Season);

        Team youngstownState = await database.Teams.SingleAsync(t => t.School == "Youngstown State");
        Team indianaState = await database.Teams.SingleAsync(t => t.School == "Indiana State");

        youngstownState.Classification.Should().Be(TeamClassification.Fcs);
        indianaState.Classification.Should().Be(TeamClassification.Fcs);
    }

    [Fact]
    public async Task GivenScheduleIngested_ThenSaturdayEasternIsComputedFromKickoffUtc()
    {
        await using AppDbContext database = _database.CreateContext();
        var service = CreateService(database, new FixtureReferenceDataProvider());

        await service.IngestTeamsAsync(Season);
        ScheduleIngestResult result = await service.IngestScheduleAsync(Season, Week);
        result.Success.Should().BeTrue();

        // 700016: USC/Stanford, kicks 2026-10-17T04:30Z = Saturday 00:30 ET (Friday-Pacific,
        // Saturday-Eastern).
        Game fridayPacific = await database.Games.SingleAsync(g => g.CfbdGameId == 700016);
        fridayPacific.IsSaturdayEastern.Should().BeTrue();
        fridayPacific.KickoffEasternDate.Should().Be(new DateOnly(2026, 10, 17));

        // 700015: kicks 2026-10-16T23:30Z = Friday 19:30 ET, a genuine Friday game.
        Game friday = await database.Games.SingleAsync(g => g.CfbdGameId == 700015);
        friday.IsSaturdayEastern.Should().BeFalse();
        friday.KickoffEasternDate.Should().Be(new DateOnly(2026, 10, 16));
    }

    [Fact]
    public async Task GivenGameMissingFromPayload_ThenPostponed_AndRestoredWhenItReappears()
    {
        await using AppDbContext database = _database.CreateContext();
        var baseProvider = new FixtureReferenceDataProvider();
        var decorated = new DecoratingReferenceDataProvider(baseProvider);
        var service = CreateService(database, decorated);

        await service.IngestTeamsAsync(Season);

        // First fetch: the game is present, as usual.
        ScheduleIngestResult present = await service.IngestScheduleAsync(Season, Week);
        present.Success.Should().BeTrue();
        (await database.Games.SingleAsync(g => g.CfbdGameId == 700016)).Status.Should().Be(GameStatus.Scheduled);

        // Second fetch: CFBD's payload no longer carries game 700016 (it disappeared, which is
        // how a Postponed game shows up on CFBD's schedule endpoint — see DECISIONS.md).
        decorated.DroppedCfbdGameIds.Add(700016);
        ScheduleIngestResult missing = await service.IngestScheduleAsync(Season, Week);
        missing.Success.Should().BeTrue();
        missing.Postponed.Should().Be(1);
        (await database.Games.SingleAsync(g => g.CfbdGameId == 700016)).Status.Should().Be(GameStatus.Postponed);

        // Third fetch: the game reappears, so it goes back to Scheduled.
        decorated.DroppedCfbdGameIds.Clear();
        ScheduleIngestResult restored = await service.IngestScheduleAsync(Season, Week);
        restored.Success.Should().BeTrue();
        restored.Restored.Should().Be(1);
        (await database.Games.SingleAsync(g => g.CfbdGameId == 700016)).Status.Should().Be(GameStatus.Scheduled);
    }

    [Fact]
    public async Task GivenProviderThrows_ThenLastErrorIsSetAndPriorDataIsUntouched()
    {
        await using AppDbContext database = _database.CreateContext();
        var baseProvider = new FixtureReferenceDataProvider();
        var decorated = new DecoratingReferenceDataProvider(baseProvider);
        var service = CreateService(database, decorated);

        await service.IngestTeamsAsync(Season);
        await service.IngestScheduleAsync(Season, Week);

        int gamesBefore = await database.Games.CountAsync(g => g.SeasonYear == Season && g.Week == Week);
        GameStatus statusBefore = (await database.Games.SingleAsync(g => g.CfbdGameId == 700001)).Status;

        decorated.Games = (_, _, _) => throw new InvalidOperationException("simulated CFBD outage");

        ScheduleIngestResult failed = await service.IngestScheduleAsync(Season, Week);

        failed.Success.Should().BeFalse();
        failed.Error.Should().Contain("simulated CFBD outage");

        (await database.Games.CountAsync(g => g.SeasonYear == Season && g.Week == Week)).Should().Be(gamesBefore);
        (await database.Games.SingleAsync(g => g.CfbdGameId == 700001)).Status.Should().Be(statusBefore);

        DataRefreshStatus status = await database.DataRefreshStatuses.SingleAsync(s => s.DataType == RefreshDataType.Schedule);
        status.LastError.Should().Contain("simulated CFBD outage");
        status.LastAttemptUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GivenAnFbsVersusFcsGame_WhenScheduleIngested_ThenItIsStoredBecauseTheFcsTeamIsKnown()
    {
        await using AppDbContext database = _database.CreateContext();
        var service = CreateService(database, new FixtureReferenceDataProvider());

        await service.IngestTeamsAsync(Season);
        await service.IngestScheduleAsync(Season, Week);

        // 700013 is Penn State (FBS) vs Youngstown State (FCS) and 700014 is Indiana vs Indiana
        // State. The reference ingest fetches every division, so both FCS schools exist and both
        // games are stored; an FBS-only teams fetch would leave the ingest unable to resolve the
        // away side and it would drop the games entirely (see DECISIONS.md).
        Game penn = await database.Games.SingleAsync(g => g.CfbdGameId == 700013);
        Team youngstown = await database.Teams.SingleAsync(t => t.Id == penn.AwayTeamId);
        youngstown.School.Should().Be("Youngstown State");
        youngstown.Classification.Should().Be(TeamClassification.Fcs);

        (await database.Games.AnyAsync(g => g.CfbdGameId == 700014)).Should().BeTrue();
    }

    [Fact]
    public async Task GivenTheFcsTeamsAreMissing_WhenScheduleIngested_ThenTheirGamesAreDropped()
    {
        await using AppDbContext database = _database.CreateContext();
        var baseProvider = new FixtureReferenceDataProvider();
        var decorated = new DecoratingReferenceDataProvider(baseProvider)
        {
            // What an FBS-only teams fetch would produce.
            Teams = async (season, ct) =>
            {
                IReadOnlyList<ProviderTeam> all = await baseProvider.GetTeamsAsync(season, ct);
                return [.. all.Where(t => t.Classification == TeamClassification.Fbs)];
            },
        };
        var service = CreateService(database, decorated);

        await service.IngestTeamsAsync(Season);
        ScheduleIngestResult result = await service.IngestScheduleAsync(Season, Week);

        result.Success.Should().BeTrue();
        (await database.Games.AnyAsync(g => g.CfbdGameId == 700013)).Should().BeFalse();
        (await database.Games.AnyAsync(g => g.CfbdGameId == 700014)).Should().BeFalse();
    }

    [Fact]
    public async Task GivenAnEmptyPayloadForAWeekWithNoRows_WhenScheduleIngested_ThenItFailsRatherThanReportingSuccess()
    {
        await using AppDbContext database = _database.CreateContext();
        var decorated = new DecoratingReferenceDataProvider(new FixtureReferenceDataProvider())
        {
            Games = (_, _, _) => Task.FromResult<IReadOnlyList<ProviderGame>>([]),
        };
        var service = CreateService(database, decorated);

        await service.IngestTeamsAsync(Season);
        ScheduleIngestResult result = await service.IngestScheduleAsync(Season, Week);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("no games at all");

        (await database.Games.CountAsync()).Should().Be(0);

        DataRefreshStatus status = await database.DataRefreshStatuses
            .SingleAsync(s => s.DataType == RefreshDataType.Schedule);
        status.LastSuccessUtc.Should().BeNull();
        status.LastAttemptUtc.Should().NotBeNull();
        status.LastError.Should().Contain("no games at all");
    }

    [Fact]
    public async Task GivenAPayloadThatOmitsOptionalFields_WhenReIngested_ThenStoredValuesAreKept()
    {
        await using AppDbContext database = _database.CreateContext();
        var baseProvider = new FixtureReferenceDataProvider();
        var decorated = new DecoratingReferenceDataProvider(baseProvider);
        var service = CreateService(database, decorated);

        await service.IngestTeamsAsync(Season);
        await service.IngestScheduleAsync(Season, Week);

        Team before = await database.Teams.SingleAsync(t => t.CfbdId == 900123);
        before.Mascot.Should().NotBeNull();
        before.Abbreviation.Should().NotBeNull();
        Game gameBefore = await database.Games.SingleAsync(g => g.CfbdGameId == 700014);
        gameBefore.Venue.Should().NotBeNull();

        // A thinner second payload: CFBD said nothing about mascot, abbreviation, logo or venue.
        decorated.Teams = async (season, ct) =>
        {
            IReadOnlyList<ProviderTeam> all = await baseProvider.GetTeamsAsync(season, ct);
            return [.. all.Select(t => t with { Mascot = null, Abbreviation = null, LogoUrl = null })];
        };
        decorated.Games = async (season, week, ct) =>
        {
            IReadOnlyList<ProviderGame> all = await baseProvider.GetGamesAsync(season, week, ct);
            return [.. all.Select(g => g with { Venue = null })];
        };

        (await service.IngestTeamsAsync(Season)).Success.Should().BeTrue();
        (await service.IngestScheduleAsync(Season, Week)).Success.Should().BeTrue();

        database.ChangeTracker.Clear();

        Team after = await database.Teams.SingleAsync(t => t.CfbdId == 900123);
        after.Mascot.Should().Be(before.Mascot);
        after.Abbreviation.Should().Be(before.Abbreviation);
        (await database.Games.SingleAsync(g => g.CfbdGameId == 700014)).Venue.Should().Be(gameBefore.Venue);
    }

    [Fact]
    public async Task GivenAnAliasAlreadyOwnedByAnotherTeam_WhenTeamsIngested_ThenItIsNotRepointed()
    {
        await using AppDbContext database = _database.CreateContext();
        var baseProvider = new FixtureReferenceDataProvider();
        var decorated = new DecoratingReferenceDataProvider(baseProvider);
        var service = CreateService(database, decorated);

        await service.IngestTeamsAsync(Season);

        Guid indianaId = (await database.Teams.SingleAsync(t => t.CfbdId == 900123)).Id;
        TeamAlias alias = await database.TeamAliases
            .SingleAsync(a => a.Source == ProviderSource.Cfbd && a.Alias == "Indiana Hoosiers");
        alias.TeamId.Should().Be(indianaId);

        // CFBD now claims "Indiana Hoosiers" as an alternate name of Indiana State. The alias is
        // unique per (Source, Alias) and may have been hand-verified by TeamAliasSeed, so the
        // ingest must leave it pointing where it is.
        decorated.Teams = async (season, ct) =>
        {
            IReadOnlyList<ProviderTeam> all = await baseProvider.GetTeamsAsync(season, ct);
            return
            [
                .. all.Select(t => t.CfbdId == 900124
                    ? t with { AlternateNames = [.. t.AlternateNames, "Indiana Hoosiers"] }
                    : t),
            ];
        };

        (await service.IngestTeamsAsync(Season)).Success.Should().BeTrue();

        database.ChangeTracker.Clear();

        TeamAlias after = await database.TeamAliases
            .SingleAsync(a => a.Source == ProviderSource.Cfbd && a.Alias == "Indiana Hoosiers");
        after.TeamId.Should().Be(indianaId);
    }

    [Fact]
    public async Task GivenScheduleIngestedTwice_ThenGameRowCountIsUnchanged()
    {
        await using AppDbContext database = _database.CreateContext();
        var service = CreateService(database, new FixtureReferenceDataProvider());

        await service.IngestTeamsAsync(Season);

        ScheduleIngestResult first = await service.IngestScheduleAsync(Season, Week);
        first.Success.Should().BeTrue();
        int gamesAfterFirst = await database.Games.CountAsync();
        gamesAfterFirst.Should().BeGreaterThan(0);

        ScheduleIngestResult second = await service.IngestScheduleAsync(Season, Week);
        second.Success.Should().BeTrue();
        second.Postponed.Should().Be(0);

        (await database.Games.CountAsync()).Should().Be(gamesAfterFirst);
    }

    [Fact]
    public async Task GivenTheShapeOfTheRealCalendar_WhenIngestedTwice_ThenOnlyRegularWeeksAreStoredOnce()
    {
        await using AppDbContext database = _database.CreateContext();

        // The real 2026 payload: 15 regular weeks numbered 1..15 plus one postseason row, which
        // CFBD also numbers 1. Before D-167 that collision made EF refuse to track two
        // SeasonWeeks with the same (SeasonYear, Week) and the whole calendar step failed.
        var decorated = new DecoratingReferenceDataProvider(new FixtureReferenceDataProvider())
        {
            Calendar = (season, _) => Task.FromResult<IReadOnlyList<ProviderCalendarWeek>>(
            [
                .. Enumerable.Range(1, 15).Select(week => CfbdWeek(season, week)),
                new ProviderCalendarWeek(
                    season,
                    1,
                    "Postseason",
                    new DateTime(2026, 12, 14, 8, 0, 0, DateTimeKind.Utc),
                    new DateTime(2027, 1, 25, 7, 59, 0, DateTimeKind.Utc)),
            ]),
        };
        var service = CreateService(database, decorated);

        CalendarIngestResult first = await service.IngestCalendarAsync(Season);
        first.Success.Should().BeTrue();
        first.Weeks.Should().Be(15);
        first.SkippedNonRegular.Should().Be(1);

        // Idempotent: the same payload again changes nothing.
        CalendarIngestResult second = await service.IngestCalendarAsync(Season);
        second.Success.Should().BeTrue();
        second.Weeks.Should().Be(15);

        database.ChangeTracker.Clear();

        List<SeasonWeek> stored = await database.SeasonWeeks
            .Where(w => w.SeasonYear == Season)
            .OrderBy(w => w.Week)
            .ToListAsync();

        stored.Should().HaveCount(15);
        stored.Select(w => w.Week).Should().Equal(Enumerable.Range(1, 15));

        // Sunday-Saturday Eastern, not CFBD's own Monday-to-Monday boundary.
        (DateTimeOffset expectedStart, DateTimeOffset expectedEnd) =
            SeasonCalendar.WeekWindow(new DateOnly(2026, 9, 5));
        stored[0].StartUtc.Should().Be(expectedStart);
        stored[0].EndUtc.Should().Be(expectedEnd);

        // Week 15 is conference-championship week: stored, but not playable, so a new league
        // defaults to weeks 1..14.
        stored[^1].IsRegularSeason.Should().BeFalse();
        stored.SkipLast(1).Should().OnlyContain(w => w.IsRegularSeason);
        SeasonCalendar.DefaultLeagueRange(stored).Should().Be(new LeagueWeekRange(1, 14));

        DataRefreshStatus status = await database.DataRefreshStatuses
            .SingleAsync(s => s.DataType == RefreshDataType.Schedule);
        status.LastSuccessUtc.Should().NotBeNull();
        status.LastError.Should().BeNull();
    }

    [Fact]
    public async Task GivenTwoTeamsClaimingOneAliasAndATeamRepeatingItsOwn_WhenTeamsIngested_ThenTheFirstClaimantWins()
    {
        await using AppDbContext database = _database.CreateContext();
        var decorated = new DecoratingReferenceDataProvider(new FixtureReferenceDataProvider())
        {
            // The collisions CFBD's real 2026 teams payload contains: the same alias claimed by
            // two schools, the same alias in two casings (IX_TeamAliases_Source_Alias is unique
            // under a case-insensitive collation), the same alias listed twice by one team, and
            // an alias that only repeats the team's own school name.
            Teams = (_, _) => Task.FromResult<IReadOnlyList<ProviderTeam>>(
            [
                new ProviderTeam(
                    990001,
                    "Alma",
                    null,
                    null,
                    null,
                    TeamClassification.Other,
                    null,
                    ["Alma College", "alma college", "Scots"]),
                new ProviderTeam(
                    990002,
                    "Alma Mater",
                    null,
                    null,
                    null,
                    TeamClassification.Other,
                    null,
                    ["ALMA COLLEGE", "Alma Mater"]),
            ]),
        };
        var service = CreateService(database, decorated);

        TeamsIngestResult result = await service.IngestTeamsAsync(Season);

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();

        database.ChangeTracker.Clear();

        Guid almaId = (await database.Teams.SingleAsync(t => t.CfbdId == 990001)).Id;
        List<TeamAlias> aliases = await database.TeamAliases
            .Where(a => a.Source == ProviderSource.Cfbd)
            .ToListAsync();

        aliases.Select(a => a.Alias).Should().BeEquivalentTo(["Alma College", "Scots"]);
        aliases.Should().OnlyContain(a => a.TeamId == almaId);

        // And again: still exactly those two rows.
        (await service.IngestTeamsAsync(Season)).Success.Should().BeTrue();
        (await database.TeamAliases.CountAsync(a => a.Source == ProviderSource.Cfbd)).Should().Be(2);
    }

    [Fact]
    public async Task GivenProviderStringsLongerThanTheirColumns_WhenTeamsIngested_ThenTheyAreTruncatedRatherThanRejected()
    {
        await using AppDbContext database = _database.CreateContext();
        var baseProvider = new FixtureReferenceDataProvider();
        var decorated = new DecoratingReferenceDataProvider(baseProvider)
        {
            Teams = async (season, ct) =>
            {
                IReadOnlyList<ProviderTeam> all = await baseProvider.GetTeamsAsync(season, ct);
                return
                [
                    .. all.Select(t => t.CfbdId == 900123
                        ? t with { School = new string('X', Team.SchoolMaxLength + 25) }
                        : t),
                ];
            },
        };
        var service = CreateService(database, decorated);

        TeamsIngestResult result = await service.IngestTeamsAsync(Season);

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();

        Team stored = await database.Teams.SingleAsync(t => t.CfbdId == 900123);
        stored.School.Should().HaveLength(Team.SchoolMaxLength);
    }

    /// <summary>
    /// A CFBD-shaped calendar row: Monday-before through Monday-after the week's Saturday, which
    /// for week 1 is 2026-09-05.
    /// </summary>
    private static ProviderCalendarWeek CfbdWeek(int season, int week)
    {
        DateOnly saturday = new DateOnly(2026, 9, 5).AddDays(7 * (week - 1));
        return new ProviderCalendarWeek(
            season,
            week,
            "Regular",
            saturday.AddDays(-5).ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc),
            saturday.AddDays(2).ToDateTime(new TimeOnly(6, 59), DateTimeKind.Utc));
    }

    [Fact]
    public async Task GivenRankingsIngestedTwice_ThenNoDuplicateRankingRows()
    {
        await using AppDbContext database = _database.CreateContext();
        var service = CreateService(database, new FixtureReferenceDataProvider());

        await service.IngestTeamsAsync(Season);
        await service.IngestScheduleAsync(Season, Week);

        RankingsIngestResult first = await service.IngestRankingsAsync(Season, Week);
        first.Success.Should().BeTrue();
        int countAfterFirst = await database.Rankings.CountAsync(r => r.SeasonYear == Season && r.Week == Week);

        RankingsIngestResult second = await service.IngestRankingsAsync(Season, Week);
        second.Success.Should().BeTrue();
        int countAfterSecond = await database.Rankings.CountAsync(r => r.SeasonYear == Season && r.Week == Week);

        countAfterSecond.Should().Be(countAfterFirst);
    }

    [Fact]
    public async Task GivenLinesIngestedTwice_ThenGameLinesGrowsOnlyWhenSpreadChanges()
    {
        await using AppDbContext database = _database.CreateContext();
        var baseProvider = new FixtureReferenceDataProvider();
        var decorated = new DecoratingReferenceDataProvider(baseProvider);
        var service = CreateService(database, decorated);

        await service.IngestTeamsAsync(Season);
        await service.IngestScheduleAsync(Season, Week);

        LinesIngestResult first = await service.IngestLinesAsync(Season, Week);
        first.Success.Should().BeTrue();
        first.Lines.Should().BeGreaterThan(0);

        Guid gameId = (await database.Games.SingleAsync(g => g.CfbdGameId == 700001)).Id;
        int lineCountAfterFirst = await database.GameLines.CountAsync(l => l.GameId == gameId);

        // Same spreads again: no new history rows.
        LinesIngestResult second = await service.IngestLinesAsync(Season, Week);
        second.Success.Should().BeTrue();
        second.Lines.Should().Be(0);
        (await database.GameLines.CountAsync(l => l.GameId == gameId)).Should().Be(lineCountAfterFirst);

        // The spread on 700001 moves: exactly one new history row for that game.
        decorated.Lines = async (season, week, ct) =>
        {
            IReadOnlyList<ProviderLine> original = await baseProvider.GetLinesAsync(season, week, ct);
            return
            [
                .. original.Select(line => line.CfbdGameId == 700001
                    ? line with { Spread = line.Spread - 1, FetchedUtc = line.FetchedUtc.AddHours(1) }
                    : line),
            ];
        };

        LinesIngestResult third = await service.IngestLinesAsync(Season, Week);
        third.Success.Should().BeTrue();
        third.Lines.Should().Be(1);
        (await database.GameLines.CountAsync(l => l.GameId == gameId)).Should().Be(lineCountAfterFirst + 1);
    }

    private static ReferenceDataIngestService CreateService(AppDbContext database, IReferenceDataProvider provider) =>
        new(database, provider, TimeProvider.System, NullLogger<ReferenceDataIngestService>.Instance);

    /// <summary>
    /// Wraps a real <see cref="IReferenceDataProvider"/> and lets a test override one method, or
    /// drop specific games from the schedule — the shapes the card's postponement and
    /// provider-failure cases need and that the fixture provider alone cannot produce.
    /// </summary>
    private sealed class DecoratingReferenceDataProvider : IReferenceDataProvider
    {
        private readonly IReferenceDataProvider _inner;

        public DecoratingReferenceDataProvider(IReferenceDataProvider inner)
        {
            _inner = inner;
        }

        public HashSet<long> DroppedCfbdGameIds { get; } = [];

        public Func<int, CancellationToken, Task<IReadOnlyList<ProviderTeam>>>? Teams { get; set; }

        public Func<int, int, CancellationToken, Task<IReadOnlyList<ProviderGame>>>? Games { get; set; }

        public Func<int, int, CancellationToken, Task<IReadOnlyList<ProviderLine>>>? Lines { get; set; }

        public Func<int, CancellationToken, Task<IReadOnlyList<ProviderCalendarWeek>>>? Calendar { get; set; }

        public Task<IReadOnlyList<ProviderConference>> GetConferencesAsync(
            int season, CancellationToken cancellationToken = default) =>
            _inner.GetConferencesAsync(season, cancellationToken);

        public Task<IReadOnlyList<ProviderTeam>> GetTeamsAsync(
            int season, CancellationToken cancellationToken = default) =>
            Teams is not null
                ? Teams(season, cancellationToken)
                : _inner.GetTeamsAsync(season, cancellationToken);

        public async Task<IReadOnlyList<ProviderGame>> GetGamesAsync(
            int season, int week, CancellationToken cancellationToken = default)
        {
            if (Games is not null)
            {
                return await Games(season, week, cancellationToken).ConfigureAwait(false);
            }

            IReadOnlyList<ProviderGame> games = await _inner.GetGamesAsync(season, week, cancellationToken)
                .ConfigureAwait(false);
            return DroppedCfbdGameIds.Count == 0
                ? games
                : [.. games.Where(g => !DroppedCfbdGameIds.Contains(g.CfbdGameId))];
        }

        public Task<IReadOnlyList<ProviderRanking>> GetRankingsAsync(
            int season, int week, CancellationToken cancellationToken = default) =>
            _inner.GetRankingsAsync(season, week, cancellationToken);

        public Task<IReadOnlyList<ProviderLine>> GetLinesAsync(
            int season, int week, CancellationToken cancellationToken = default) =>
            Lines is not null
                ? Lines(season, week, cancellationToken)
                : _inner.GetLinesAsync(season, week, cancellationToken);

        public Task<IReadOnlyList<ProviderCalendarWeek>> GetCalendarAsync(
            int season, CancellationToken cancellationToken = default) =>
            Calendar is not null
                ? Calendar(season, cancellationToken)
                : _inner.GetCalendarAsync(season, cancellationToken);
    }
}
