using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Invites;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Contracts.Points;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Plays the other members: creates players, creates leagues, joins them through a real invite,
/// and makes, submits and clears picks through <see cref="PickService"/> - so the lock, the
/// current-week rule and submission status behave exactly as for a member on a phone.
/// </summary>
public sealed class PeopleDesk
{
    /// <summary>
    /// Subject prefix of simulator players; <c>/auth/dev-login?user=&lt;slug&gt;</c> signs them in
    /// (D-184).
    /// </summary>
    public const string SubjectPrefix = "sim:";

    private readonly AppDbContext _database;
    private readonly LeagueService _leagues;
    private readonly InviteService _invites;
    private readonly PickService _picks;
    private readonly GameSetService _gameSets;
    private readonly PointRuleService _pointRules;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the desk. Every dependency is the step's own scope.</summary>
    public PeopleDesk(
        AppDbContext database,
        LeagueService leagues,
        InviteService invites,
        PickService picks,
        GameSetService gameSets,
        PointRuleService pointRules,
        TimeProvider timeProvider)
    {
        _database = database;
        _leagues = leagues;
        _invites = invites;
        _picks = picks;
        _gameSets = gameSets;
        _pointRules = pointRules;
        _timeProvider = timeProvider;
    }

    /// <summary>The dev-login name for a display name: lower-case letters and digits only.</summary>
    public static string SlugFor(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var slug = new StringBuilder();
        foreach (char character in displayName.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                slug.Append(character);
            }
        }

        return slug.Length == 0 ? "player" : slug.ToString();
    }

    /// <summary>The player called <paramref name="displayName"/>, created on first use.</summary>
    public async Task<User> EnsureUserAsync(string displayName, CancellationToken cancellationToken)
    {
        string name = displayName.Trim();
        if (name.Length > User.DisplayNameMaxLength)
        {
            name = name[..User.DisplayNameMaxLength];
        }

        string subject = SubjectPrefix + SlugFor(name);
        User? user = await _database.Users
            .FirstOrDefaultAsync(candidate => candidate.ExternalSubject == subject, cancellationToken)
            .ConfigureAwait(false);

        if (user is not null)
        {
            return user;
        }

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        user = new User
        {
            Id = Guid.CreateVersion7(),
            ExternalSubject = subject,
            Email = $"{SlugFor(name)}@simulator.local",
            DisplayName = name,
            CreatedUtc = nowUtc,
            LastLoginUtc = nowUtc,
        };
        _database.Users.Add(user);
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return user;
    }

    /// <summary>Creates a league with <paramref name="commissionerUserId"/> as its commissioner, through <see cref="LeagueService"/>.</summary>
    public async Task<Guid> CreateLeagueAsync(Guid commissionerUserId, string name, int seasonYear, CancellationToken cancellationToken)
    {
        LeagueDetail league = await _leagues
            .CreateAsync(commissionerUserId, new CreateLeagueRequest(name, seasonYear, null, null), cancellationToken)
            .ConfigureAwait(false);
        return league.LeagueId;
    }

    /// <summary>
    /// Joins a player to a league the way a member does: a commissioner's fresh invite, accepted by
    /// the player through <see cref="InviteService.AcceptAsync"/>.
    /// </summary>
    /// <returns>What happened, for the activity log.</returns>
    public async Task<string> JoinAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken)
    {
        Membership commissioner = await _database.Memberships
            .FirstAsync(
                membership => membership.LeagueId == leagueId
                    && membership.Role == MembershipRole.Commissioner
                    && membership.RemovedUtc == null,
                cancellationToken)
            .ConfigureAwait(false);

        InviteResponse invite = await _invites.CreateAsync(commissioner, "https://localhost", cancellationToken).ConfigureAwait(false);
        return await JoinWithCodeAsync(invite.Code, userId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Accepts an invite code as the player - e.g. a code the tester made in the app.</summary>
    public async Task<string> JoinWithCodeAsync(string code, Guid userId, CancellationToken cancellationToken)
    {
        InviteAcceptResult? result = await _invites.AcceptAsync(code.Trim(), userId, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            null => $"no invite with code {code}",
            { Succeeded: true, League: LeagueDetail league } => $"joined {league.Name} (week {league.CurrentWeek})",
            { Conflict: InvitePreview conflict } => $"could not join {conflict.LeagueName}: invite is {conflict.State}",
            _ => "could not join",
        };
    }

    /// <summary>
    /// The default rules a sandbox league gets: a Top 25 game-set rule plus a whole-conference
    /// rule, and point rules for close lines and conference games (so elevated values show).
    /// </summary>
    public async Task ApplyDefaultRulesAsync(Guid leagueId, string conferenceAbbreviation, CancellationToken cancellationToken)
    {
        Guid? conferenceId = await _database.Conferences
            .Where(conference => conference.Abbreviation == conferenceAbbreviation)
            .Select(conference => (Guid?)conference.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        List<GameSetRuleDto> rules = [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)];
        if (conferenceId is Guid id)
        {
            rules.Add(new GameSetRuleDto(null, GameSetRuleType.Conference, id, null, null, null, true, 1));
        }

        await _gameSets.ReplaceDefaultRulesAsync(leagueId, rules, cancellationToken).ConfigureAwait(false);

        await _pointRules.ReplaceRulesAsync(
            leagueId,
            [
                new PointRuleDto(null, 1, PointRuleType.CloseSpread, null, null, SlateBuilder.CloseSpread, 20),
                new PointRuleDto(null, 2, PointRuleType.ConferenceGame, null, null, null, 15),
            ],
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Makes (or changes) one pick for a member, through <see cref="PickService.SetPickAsync"/>.</summary>
    public async Task<MyPicksResponse> SetPickAsync(Guid membershipId, int week, Guid gameId, Guid teamId, CancellationToken cancellationToken)
    {
        Membership membership = await LoadMembershipAsync(membershipId, cancellationToken).ConfigureAwait(false);
        return await _picks.SetPickAsync(membership, week, gameId, teamId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Presses Submit for a member.</summary>
    public async Task<MyPicksResponse> SubmitAsync(Guid membershipId, int week, CancellationToken cancellationToken)
    {
        Membership membership = await LoadMembershipAsync(membershipId, cancellationToken).ConfigureAwait(false);
        return await _picks.SubmitAsync(membership, week, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Picks every game the member has not picked yet (leaving the last
    /// <paramref name="leaveUnpicked"/> alone), then submits if asked and the member picked
    /// everything.
    /// </summary>
    /// <param name="membershipId">The member.</param>
    /// <param name="week">The current week.</param>
    /// <param name="strategy">How to choose sides.</param>
    /// <param name="variant">The member's position in the league, which staggers <see cref="PickStrategy.Mixed"/>.</param>
    /// <param name="leaveUnpicked">Games to leave open, to test an incomplete member.</param>
    /// <param name="submit">Press Submit afterwards.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What happened, for the activity log.</returns>
    public async Task<string> FillAsync(
        Guid membershipId,
        int week,
        PickStrategy strategy,
        int variant,
        int leaveUnpicked,
        bool submit,
        CancellationToken cancellationToken)
    {
        Membership membership = await LoadMembershipAsync(membershipId, cancellationToken).ConfigureAwait(false);
        MyPicksResponse mine = await _picks.GetMyPicksAsync(membership, week, cancellationToken).ConfigureAwait(false);

        if (mine.TotalCount == 0)
        {
            return "the week has no games to pick yet";
        }

        // Positions in the week's full game list, so Mixed flips the same games however many are
        // already picked.
        List<(GameSetGameDto Game, int Index)> open =
        [
            .. mine.Games
                .Select((row, index) => (row, index))
                .Where(entry => !entry.row.Game.IsVoided && entry.row.MyTeamId is null)
                .Select(entry => (entry.row.Game, entry.index)),
        ];

        int made = 0;
        foreach ((GameSetGameDto game, int index) in open.Take(Math.Max(0, open.Count - Math.Max(0, leaveUnpicked))))
        {
            mine = await _picks
                .SetPickAsync(membership, week, game.GameId, Choose(game, strategy, index, variant), cancellationToken)
                .ConfigureAwait(false);
            made++;
        }

        if (submit && mine.PickedCount == mine.TotalCount && mine.Status != SubmissionStatus.Submitted)
        {
            mine = await _picks.SubmitAsync(membership, week, cancellationToken).ConfigureAwait(false);
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{made} pick(s), {mine.PickedCount}/{mine.TotalCount} picked, {mine.Status}");
    }

    /// <summary>
    /// Deletes every pick the member has in the week's set and un-submits them - there is no
    /// "unpick" in the app, so this is the simulator's own reset - then lets
    /// <see cref="PickService"/> recompute the status. Refused once the week is locked.
    /// </summary>
    public async Task<string> ClearAsync(Guid membershipId, int week, CancellationToken cancellationToken)
    {
        Membership membership = await LoadMembershipAsync(membershipId, cancellationToken).ConfigureAwait(false);
        WeekGameSet? set = await _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.LeagueId == membership.LeagueId && candidate.Week == week, cancellationToken)
            .ConfigureAwait(false);

        if (set is null)
        {
            return "no set this week";
        }

        if (WeekGameSetLockGuard.IsFrozen(set, _timeProvider.GetUtcNow().UtcDateTime))
        {
            return "the week is locked; rewind it first";
        }

        int deleted = await _database.Picks
            .Where(pick => pick.MembershipId == membershipId && pick.WeekGameSetGame!.WeekGameSetId == set.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await _database.WeekSubmissions
            .Where(row => row.MembershipId == membershipId && row.WeekGameSetId == set.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SubmittedUtc, (DateTime?)null), cancellationToken)
            .ConfigureAwait(false);

        await _picks.RecomputeStatusAsync(set.Id, membershipId, markUnseenGameChanges: false, cancellationToken).ConfigureAwait(false);
        return $"cleared {deleted} pick(s)";
    }

    /// <summary>The side <paramref name="strategy"/> picks for one game.</summary>
    public static Guid Choose(GameSetGameDto game, PickStrategy strategy, int gameIndex, int variant)
    {
        ArgumentNullException.ThrowIfNull(game);

        bool homeIsFavourite = game.Spread switch
        {
            < 0 => true,
            > 0 => false,
            _ => (game.HomeRank, game.AwayRank) switch
            {
                (int home, int away) => home <= away,
                (null, int) => false,
                _ => true,
            },
        };

        bool pickHome = strategy switch
        {
            PickStrategy.Favorites => homeIsFavourite,
            PickStrategy.Underdogs => !homeIsFavourite,
            PickStrategy.HomeTeams => true,
            PickStrategy.Random => ((ScorePlan.SeedFor(game.GameId) ^ (variant * 7919)) & 1) == 0,
            _ => homeIsFavourite != (variant > 0 && (gameIndex + variant) % 3 == 0),
        };

        return pickHome ? game.HomeTeam.TeamId : game.AwayTeam.TeamId;
    }

    private Task<Membership> LoadMembershipAsync(Guid membershipId, CancellationToken cancellationToken) =>
        _database.Memberships
            .Include(membership => membership.User)
            .FirstAsync(membership => membership.Id == membershipId, cancellationToken);
}
