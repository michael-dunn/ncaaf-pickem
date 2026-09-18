using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Seeding;

/// <summary>
/// Loads the Week 7, 2026 fixture reference data into the database when it is empty, and
/// optionally creates the Feature 05 worked-example demo league. Both halves are idempotent, so
/// calling this on every startup is safe (P2-05).
/// </summary>
/// <remarks>
/// Runs only when <c>Providers:ReferenceData</c> is <c>Fixture</c> — anything else means a real
/// provider owns the data and this seeder must not race it. The demo league is gated separately
/// on <c>Seed:DemoLeague</c> so a fixture-backed dev box can have data without a league, or vice
/// versa is never needed but the flags stay orthogonal on purpose.
/// </remarks>
public sealed class FixtureSeeder
{
    /// <summary>The worked example's members, in the order WorkItems/Overview.txt lists them.</summary>
    public static readonly IReadOnlyList<string> DemoMembers = ["Michael", "Alyson", "Dance", "Alex", "Daniel"];

    /// <summary>Name of the demo league created when <c>Seed:DemoLeague</c> is true.</summary>
    public const string DemoLeagueName = "Family League";

    private readonly AppDbContext _database;
    private readonly IReferenceDataProvider _referenceData;
    private readonly ISeasonWeekSource _seasonWeekSource;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FixtureSeeder> _logger;

    /// <summary>Creates the seeder.</summary>
    public FixtureSeeder(
        AppDbContext database,
        IReferenceDataProvider referenceData,
        ISeasonWeekSource seasonWeekSource,
        TimeProvider timeProvider,
        ILogger<FixtureSeeder> logger)
    {
        _database = database;
        _referenceData = referenceData;
        _seasonWeekSource = seasonWeekSource;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Loads reference data if the database has none, then seeds the demo league if asked.</summary>
    public async Task SeedAsync(bool seedDemoLeague, CancellationToken cancellationToken = default)
    {
        await SeedReferenceDataAsync(cancellationToken).ConfigureAwait(false);

        if (seedDemoLeague)
        {
            await SeedDemoLeagueAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SeedReferenceDataAsync(CancellationToken cancellationToken)
    {
        if (await _database.Teams.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Reference data already present; skipping fixture load");
            return;
        }

        await SeedSeasonWeeksAsync(cancellationToken).ConfigureAwait(false);

        int season = FixtureReferenceDataProvider.FixtureSeason;
        int week = FixtureReferenceDataProvider.FixtureWeek;

        IReadOnlyList<ProviderConference> providerConferences =
            await _referenceData.GetConferencesAsync(season, cancellationToken).ConfigureAwait(false);
        Dictionary<int, Guid> conferenceIds = [];
        foreach (ProviderConference c in providerConferences)
        {
            var conference = new Conference
            {
                Id = Guid.CreateVersion7(),
                CfbdId = c.CfbdId,
                Name = c.Name,
                Abbreviation = c.Abbreviation,
                Classification = c.Classification,
            };
            _database.Conferences.Add(conference);
            conferenceIds[c.CfbdId] = conference.Id;
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ProviderTeam> providerTeams =
            await _referenceData.GetTeamsAsync(season, cancellationToken).ConfigureAwait(false);
        Dictionary<int, Guid> teamIds = [];
        foreach (ProviderTeam t in providerTeams)
        {
            Guid? conferenceId = t.ConferenceCfbdId is int cfbdConferenceId
                && conferenceIds.TryGetValue(cfbdConferenceId, out Guid mappedId)
                    ? mappedId
                    : null;

            var team = new Team
            {
                Id = Guid.CreateVersion7(),
                CfbdId = t.CfbdId,
                School = t.School,
                Mascot = t.Mascot,
                Abbreviation = t.Abbreviation,
                ConferenceId = conferenceId,
                Classification = t.Classification,
                LogoUrl = t.LogoUrl,
            };
            _database.Teams.Add(team);
            teamIds[t.CfbdId] = team.Id;
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ProviderGame> providerGames =
            await _referenceData.GetGamesAsync(season, week, cancellationToken).ConfigureAwait(false);
        Dictionary<long, Guid> gameIds = [];
        foreach (ProviderGame g in providerGames)
        {
            if (!teamIds.TryGetValue(g.HomeCfbdTeamId, out Guid homeTeamId)
                || !teamIds.TryGetValue(g.AwayCfbdTeamId, out Guid awayTeamId))
            {
                _logger.LogWarning(
                    "Skipping fixture game {CfbdGameId}: home or away team not in the fixture team set",
                    g.CfbdGameId);
                continue;
            }

            DateTimeOffset kickoffOffset = new(DateTime.SpecifyKind(g.KickoffUtc, DateTimeKind.Utc));
            DateOnly kickoffEasternDate = DateOnly.FromDateTime(SeasonCalendar.ToEastern(kickoffOffset).DateTime);

            var game = new Domain.Seasons.Game
            {
                Id = Guid.CreateVersion7(),
                CfbdGameId = g.CfbdGameId,
                SeasonYear = g.Season,
                Week = g.Week,
                HomeTeamId = homeTeamId,
                AwayTeamId = awayTeamId,
                KickoffUtc = g.KickoffUtc,
                KickoffEasternDate = kickoffEasternDate,
                IsSaturdayEastern = SeasonCalendar.IsSaturdayEastern(kickoffOffset),
                IsConferenceGame = g.IsConferenceGame,
                Status = g.Completed ? GameStatus.Final : GameStatus.Scheduled,
                HomeScore = g.HomePoints,
                AwayScore = g.AwayPoints,
                Venue = g.Venue,
            };
            _database.Games.Add(game);
            gameIds[g.CfbdGameId] = game.Id;
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ProviderRanking> providerRankings =
            await _referenceData.GetRankingsAsync(season, week, cancellationToken).ConfigureAwait(false);
        DateTime rankingsFetchedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (ProviderRanking r in providerRankings)
        {
            if (!teamIds.TryGetValue(r.CfbdTeamId, out Guid teamId))
            {
                // Fillers not present in the trimmed team set (see rankings.json) — expected.
                continue;
            }

            _database.Rankings.Add(new Ranking
            {
                SeasonYear = r.Season,
                Week = r.Week,
                Poll = r.Poll,
                Rank = r.Rank,
                TeamId = teamId,
                FetchedUtc = rankingsFetchedUtc,
            });
        }

        IReadOnlyList<ProviderLine> providerLines =
            await _referenceData.GetLinesAsync(season, week, cancellationToken).ConfigureAwait(false);
        foreach (ProviderLine l in providerLines)
        {
            if (l.Spread is null || !gameIds.TryGetValue(l.CfbdGameId, out Guid gameId))
            {
                continue;
            }

            _database.GameLines.Add(new GameLine
            {
                Id = Guid.CreateVersion7(),
                GameId = gameId,
                Provider = l.Provider,
                Spread = l.Spread.Value,
                FetchedUtc = l.FetchedUtc,
            });
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Seeded fixture reference data: {Conferences} conferences, {Teams} teams, {Games} games, " +
            "{Rankings} rankings, {Lines} lines",
            providerConferences.Count,
            providerTeams.Count,
            gameIds.Count,
            providerRankings.Count,
            providerLines.Count);
    }

    private async Task SeedSeasonWeeksAsync(CancellationToken cancellationToken)
    {
        if (await _database.SeasonWeeks.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        IReadOnlyList<Domain.Seasons.SeasonWeek> weeks = await _seasonWeekSource
            .GetWeeksAsync(FixtureReferenceDataProvider.FixtureSeason, cancellationToken)
            .ConfigureAwait(false);

        foreach (Domain.Seasons.SeasonWeek week in weeks)
        {
            _database.SeasonWeeks.Add(week);
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedDemoLeagueAsync(CancellationToken cancellationToken)
    {
        League? existing = await _database.Leagues
            .FirstOrDefaultAsync(l => l.Name == DemoLeagueName, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _logger.LogInformation("Demo league already exists; skipping demo league seed");
            return;
        }

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var users = new List<User>();
        foreach (string name in DemoMembers)
        {
            string subject = $"fixture:{name.ToLowerInvariant()}";
            User? user = await _database.Users
                .FirstOrDefaultAsync(u => u.GoogleSubject == subject, cancellationToken)
                .ConfigureAwait(false);

            if (user is null)
            {
                user = new User
                {
                    Id = Guid.CreateVersion7(),
                    GoogleSubject = subject,
                    Email = $"{name.ToLowerInvariant()}@fixture.local",
                    DisplayName = name,
                    CreatedUtc = nowUtc,
                    LastLoginUtc = nowUtc,
                };
                _database.Users.Add(user);
            }

            users.Add(user);
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        User michael = users[0];

        // 04-Domain-Algorithms.md section 1: default range is Week 1 through the last regular
        // week. The fixture calendar's last regular week is 14 (championship is week 15).
        var league = new League
        {
            Id = Guid.CreateVersion7(),
            Name = DemoLeagueName,
            SeasonYear = FixtureReferenceDataProvider.FixtureSeason,
            FirstWeek = 1,
            LastWeek = 14,
            DefaultPointValue = 10,
            CreatedByUserId = michael.Id,
            CreatedUtc = nowUtc,
        };
        _database.Leagues.Add(league);

        foreach (User user in users)
        {
            _database.Memberships.Add(new Membership
            {
                Id = Guid.CreateVersion7(),
                LeagueId = league.Id,
                UserId = user.Id,
                Role = user == michael ? MembershipRole.Commissioner : MembershipRole.Member,
                JoinedUtc = nowUtc,
                JoinedWeek = 1,
            });
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Seeded demo league {LeagueName} with {MemberCount} members",
            DemoLeagueName,
            users.Count);
    }
}
