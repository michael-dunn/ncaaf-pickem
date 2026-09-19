using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// A locked Week 7, 2026 game set over every fixture game the score snapshots actually carry,
/// with three members whose picks make the arithmetic obvious and one who joined too late to have
/// a row - the starting point a scoring test needs.
/// </summary>
/// <remarks>
/// <para>
/// Seeded directly rather than through <c>GameSetService</c>/<c>PickService</c>/<c>LockWeekJob</c>:
/// those are covered by their own suites, and scoring only reads what they leave behind, so
/// direct rows give exact control over the frozen point values and the settled statuses without
/// dragging in the rule engine or a clock inside the pick window.
/// </para>
/// <para>
/// Use it against a factory with its own throwaway database
/// (<see cref="SqlTestDatabase.CreateAsync"/>): advancing <see cref="FixtureSnapshotState"/>
/// writes real scores onto shared <c>Games</c> rows, which must never leak into the run's shared
/// fixture database.
/// </para>
/// </remarks>
/// <param name="LeagueId">The seeded league.</param>
/// <param name="CommissionerUserId">The commissioner's user id, for an admin-scoped client.</param>
/// <param name="WeekGameSetId">Its locked week 7 set.</param>
/// <param name="HomePickerMembershipId">Picked the home team in every game; status Locked.</param>
/// <param name="AwayPickerMembershipId">Picked the away team in every game; status Locked.</param>
/// <param name="PartialMembershipId">Picked Michigan only; status Incomplete.</param>
/// <param name="LateJoinerMembershipId">A membership with no <c>WeekSubmissions</c> row at all.</param>
/// <param name="TieGameSetGameId">The Iowa State / Kansas row, which goes Final as a tie.</param>
/// <param name="TieAwayTeamId">Kansas, the away side of that tie.</param>
/// <param name="LateGameSetGameId">The San José State / Hawai'i row, Final only in snapshot 6.</param>
public sealed record ScoredWeekScenario(
    Guid LeagueId,
    Guid CommissionerUserId,
    Guid WeekGameSetId,
    Guid HomePickerMembershipId,
    Guid AwayPickerMembershipId,
    Guid PartialMembershipId,
    Guid LateJoinerMembershipId,
    Guid TieGameSetGameId,
    Guid TieAwayTeamId,
    Guid LateGameSetGameId)
{
    /// <summary>The fixture week every scenario is built for.</summary>
    public const int Week = FixtureGameData.Week;

    /// <summary>Every fixture Week 7 kickoff lands on this Eastern calendar date.</summary>
    public static readonly DateOnly FixtureSaturday = new(2026, 10, 17);

    /// <summary>What every game in the set is worth, except the two the tests single out.</summary>
    public const int StandardPointValue = 10;

    /// <summary>What the Iowa State / Kansas tie is worth once a commissioner settles it.</summary>
    public const int TiePointValue = 15;

    /// <summary>What the post-midnight San José State / Hawai'i finish is worth.</summary>
    public const int LateGamePointValue = 35;

    /// <summary>
    /// Games in the set: the 13 Saturday-Eastern FBS-vs-FBS fixture games, which are exactly the
    /// ones the score snapshots carry live scores for. The two FCS games and the Friday game are
    /// out of the generator's eligible pool anyway.
    /// </summary>
    public const int GameCount = 13;

    /// <summary>Seeds the fixture reference data, the league, its members, and the locked set.</summary>
    /// <param name="factory">An app over a throwaway database.</param>
    public static async Task<ScoredWeekScenario> CreateAsync(ApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await FixtureGameData.EnsureSeededAsync(factory);

        return await factory.QueryDbAsync(async database =>
        {
            List<Game> games = await database.Games
                .Include(game => game.HomeTeam)
                .Include(game => game.AwayTeam)
                .Where(game => game.SeasonYear == FixtureSeasonWeekSource.FixtureSeasonYear
                    && game.Week == Week
                    && game.IsSaturdayEastern
                    && game.HomeTeam!.Classification == TeamClassification.Fbs
                    && game.AwayTeam!.Classification == TeamClassification.Fbs)
                .OrderBy(game => game.KickoffUtc)
                .ToListAsync();

            games.Should().HaveCount(GameCount);

            User owner = await TestUsers.CreateUserAsync(database);
            League league = await TestUsers.CreateLeagueAsync(database, owner);

            Membership homePicker = await TestUsers.CreateMembershipAsync(
                database, league, owner, MembershipRole.Commissioner);
            Membership awayPicker = await TestUsers.CreateMembershipAsync(
                database, league, await TestUsers.CreateUserAsync(database));
            Membership partial = await TestUsers.CreateMembershipAsync(
                database, league, await TestUsers.CreateUserAsync(database));
            Membership lateJoiner = await TestUsers.CreateMembershipAsync(
                database, league, await TestUsers.CreateUserAsync(database));

            DateTime lockAtUtc = games.Min(game => game.KickoffUtc);
            var set = new WeekGameSet
            {
                Id = Guid.CreateVersion7(),
                LeagueId = league.Id,
                Week = Week,
                GeneratedUtc = lockAtUtc.AddDays(-3),
                LockAtUtc = lockAtUtc,

                // The lock job has run: point values and spreads are frozen and every member's
                // status has been settled, which is the only state scoring ever looks at.
                LockedUtc = lockAtUtc,
            };
            database.WeekGameSets.Add(set);

            Guid tieRowId = Guid.Empty;
            Guid tieAwayTeamId = Guid.Empty;
            Guid lateRowId = Guid.Empty;
            Guid michiganRowId = Guid.Empty;

            foreach (Game game in games)
            {
                var row = new WeekGameSetGame
                {
                    Id = Guid.CreateVersion7(),
                    WeekGameSetId = set.Id,
                    GameId = game.Id,
                    Source = GameSetGameSource.Rule,
                    AddedUtc = set.GeneratedUtc,
                    ResolvedPointValue = PointValueFor(game.CfbdGameId),
                };

                database.WeekGameSetGames.Add(row);

                switch (game.CfbdGameId)
                {
                    case TieCfbdGameId:
                        tieRowId = row.Id;
                        tieAwayTeamId = game.AwayTeamId;
                        break;

                    case LateCfbdGameId:
                        lateRowId = row.Id;
                        break;

                    case MichiganCfbdGameId:
                        michiganRowId = row.Id;
                        break;

                    default:
                        break;
                }

                AddPick(database, homePicker.Id, row.Id, game.HomeTeamId);
                AddPick(database, awayPicker.Id, row.Id, game.AwayTeamId);
            }

            // One member who never finished: a single pick, and the status the lock job writes for
            // anyone who had not pressed Submit.
            AddPick(database, partial.Id, michiganRowId, games.Single(g => g.CfbdGameId == MichiganCfbdGameId).HomeTeamId);

            AddSubmission(database, set.Id, homePicker.Id, SubmissionStatus.Locked, lockAtUtc);
            AddSubmission(database, set.Id, awayPicker.Id, SubmissionStatus.Locked, lockAtUtc);
            AddSubmission(database, set.Id, partial.Id, SubmissionStatus.Incomplete, lockAtUtc);

            // lateJoiner deliberately gets no WeekSubmissions row: D-111's "joined after lock, so
            // no row", which must mean no WeekResults row either.
            await database.SaveChangesAsync();

            return new ScoredWeekScenario(
                league.Id,
                owner.Id,
                set.Id,
                homePicker.Id,
                awayPicker.Id,
                partial.Id,
                lateJoiner.Id,
                tieRowId,
                tieAwayTeamId,
                lateRowId);
        });
    }

    private const long MichiganCfbdGameId = 700001;

    private const long LateCfbdGameId = 700004;

    private const long TieCfbdGameId = 700011;

    private static int PointValueFor(long cfbdGameId) => cfbdGameId switch
    {
        TieCfbdGameId => TiePointValue,
        LateCfbdGameId => LateGamePointValue,
        _ => StandardPointValue,
    };

    private static void AddPick(
        AppDbContext database,
        Guid membershipId,
        Guid weekGameSetGameId,
        Guid pickedTeamId) =>
        database.Picks.Add(new Pick
        {
            Id = Guid.CreateVersion7(),
            MembershipId = membershipId,
            WeekGameSetGameId = weekGameSetGameId,
            PickedTeamId = pickedTeamId,
            UpdatedUtc = DateTime.UtcNow,
        });

    private static void AddSubmission(
        AppDbContext database,
        Guid weekGameSetId,
        Guid membershipId,
        SubmissionStatus status,
        DateTime lockAtUtc) =>
        database.WeekSubmissions.Add(new WeekSubmission
        {
            WeekGameSetId = weekGameSetId,
            MembershipId = membershipId,
            Status = status,
            SubmittedUtc = status == SubmissionStatus.Locked ? lockAtUtc.AddHours(-2) : null,
            LastChangedUtc = lockAtUtc,
            HasUnseenGameChanges = false,
        });
}
