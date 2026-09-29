using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Contracts.Picks;

namespace NcaafPickEm.Simulator.Engine;

/// <content>Players: joining leagues, and every member's picks.</content>
public sealed partial class SimWorld
{
    /// <summary>Creates a player (or reuses one by name) and joins them to the league through a fresh invite.</summary>
    public Task<SimLog> AddPlayerAsync(Guid leagueId, string name, CancellationToken cancellationToken) =>
        RunAsync($"Add {name}", async (log, token) =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                log.Fail("give the player a name");
                return;
            }

            string outcome = await InScopeAsync(async services =>
            {
                PeopleDesk people = services.GetRequiredService<PeopleDesk>();
                Guid userId = (await people.EnsureUserAsync(name, token).ConfigureAwait(false)).Id;
                return await people.JoinAsync(leagueId, userId, token).ConfigureAwait(false);
            }).ConfigureAwait(false);

            log.Note($"{name} {outcome} - sign in as '{PeopleDesk.SlugFor(name)}'. A member who joins after a week locks is not in that week");
        }, cancellationToken);

    /// <summary>Joins a player to whatever league an invite code belongs to - e.g. one made in the app.</summary>
    public Task<SimLog> JoinWithCodeAsync(string code, string name, CancellationToken cancellationToken) =>
        RunAsync($"{name} joins with {code}", async (log, token) =>
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
            {
                log.Fail("give both a code and a player name");
                return;
            }

            string outcome = await InScopeAsync(async services =>
            {
                PeopleDesk people = services.GetRequiredService<PeopleDesk>();
                Guid userId = (await people.EnsureUserAsync(name, token).ConfigureAwait(false)).Id;
                return await people.JoinWithCodeAsync(code, userId, token).ConfigureAwait(false);
            }).ConfigureAwait(false);

            log.Note($"{name} {outcome}");
        }, cancellationToken);

    /// <summary>Makes one pick for a member this week, as they would on the picks page.</summary>
    public Task<SimLog> SetPickAsync(Guid membershipId, Guid gameId, Guid teamId, CancellationToken cancellationToken) =>
        MemberActionAsync("Pick", membershipId, async (week, name, log, token) =>
        {
            MyPicksResponse picks = await InScopeAsync(services => services.GetRequiredService<PeopleDesk>()
                .SetPickAsync(membershipId, week, gameId, teamId, token)).ConfigureAwait(false);
            log.Note($"{name}: {picks.PickedCount}/{picks.TotalCount} picked, {picks.Status}");
        }, cancellationToken);

    /// <summary>Presses Submit for a member.</summary>
    public Task<SimLog> SubmitAsync(Guid membershipId, CancellationToken cancellationToken) =>
        MemberActionAsync("Submit", membershipId, async (week, name, log, token) =>
        {
            MyPicksResponse picks = await InScopeAsync(services => services.GetRequiredService<PeopleDesk>()
                .SubmitAsync(membershipId, week, token)).ConfigureAwait(false);
            log.Note($"{name}: {picks.Status}");
        }, cancellationToken);

    /// <summary>Fills one member's missing picks.</summary>
    public Task<SimLog> FillMemberAsync(Guid membershipId, PickStrategy strategy, int leaveUnpicked, bool submit, CancellationToken cancellationToken) =>
        MemberActionAsync($"Fill ({strategy})", membershipId, async (week, name, log, token) =>
        {
            int variant = await InScopeAsync(async services =>
            {
                AppDbContext database = services.GetRequiredService<AppDbContext>();
                var member = await database.Memberships.AsNoTracking()
                    .Where(membership => membership.Id == membershipId)
                    .Select(membership => new { membership.LeagueId, membership.JoinedUtc })
                    .FirstAsync(token).ConfigureAwait(false);
                return await database.Memberships
                    .CountAsync(membership => membership.LeagueId == member.LeagueId && membership.JoinedUtc < member.JoinedUtc, token)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);

            string outcome = await InScopeAsync(services => services.GetRequiredService<PeopleDesk>()
                .FillAsync(membershipId, week, strategy, variant, leaveUnpicked, submit, token)).ConfigureAwait(false);
            log.Note($"{name}: {outcome}");
        }, cancellationToken);

    /// <summary>
    /// Fills every member's missing picks this week, in every league. The tester is left alone
    /// unless <paramref name="includeTester"/>; <paramref name="leaveOneIncomplete"/> leaves two
    /// games open for the last member of each league to join, so there is always someone incomplete to see.
    /// </summary>
    public Task<SimLog> FillEveryoneAsync(PickStrategy strategy, bool includeTester, bool submit, bool leaveOneIncomplete, CancellationToken cancellationToken) =>
        RunAsync($"Fill everyone ({strategy})", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            if (await RequireWeekAsync(state, log, token).ConfigureAwait(false) is not int week)
            {
                return;
            }

            int filled = await FillEveryoneAsync(state, week, strategy, includeTester, 0, submit, log, token).ConfigureAwait(false);
            log.Note($"{filled} player(s) filled in{(includeTester ? string.Empty : " (not you)")}{(submit ? " and submitted" : string.Empty)}");

            if (leaveOneIncomplete)
            {
                await LeaveLastIncompleteAsync(state, week, log, token).ConfigureAwait(false);
            }
        }, cancellationToken);

    /// <summary>Deletes one member's picks this week and un-submits them.</summary>
    public Task<SimLog> ClearPicksAsync(Guid membershipId, CancellationToken cancellationToken) =>
        MemberActionAsync("Clear picks", membershipId, async (week, name, log, token) =>
        {
            string outcome = await InScopeAsync(services => services.GetRequiredService<PeopleDesk>()
                .ClearAsync(membershipId, week, token)).ConfigureAwait(false);
            log.Note($"{name}: {outcome}");
        }, cancellationToken);

    /// <summary>The last non-tester member of each league loses their picks on the week's last game.</summary>
    private async Task LeaveLastIncompleteAsync(SimState state, int week, SimLog log, CancellationToken cancellationToken)
    {
        var members = await InScopeAsync(services => services.GetRequiredService<AppDbContext>().Memberships
            .AsNoTracking()
            .Where(membership => membership.League!.SeasonYear == state.SeasonYear
                && membership.RemovedUtc == null
                && membership.UserId != state.TesterUserId)
            .Select(membership => new
            {
                membership.Id,
                membership.LeagueId,
                membership.JoinedUtc,
                Name = membership.DisplayNameOverride ?? membership.User!.DisplayName,
            })
            .ToListAsync(cancellationToken)).ConfigureAwait(false);

        foreach (var victim in members.GroupBy(member => member.LeagueId).Select(league => league.MaxBy(member => member.JoinedUtc)!))
        {
            string outcome = await InScopeAsync(async services =>
            {
                PeopleDesk people = services.GetRequiredService<PeopleDesk>();
                await people.ClearAsync(victim.Id, week, cancellationToken).ConfigureAwait(false);
                return await people.FillAsync(victim.Id, week, PickStrategy.Mixed, 1, leaveUnpicked: 2, submit: true, cancellationToken)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);
            log.Note($"{victim.Name} left incomplete: {outcome}");
        }
    }

    private Task<SimLog> MemberActionAsync(
        string title,
        Guid membershipId,
        Func<int, string, SimLog, CancellationToken, Task> body,
        CancellationToken cancellationToken) =>
        RunAsync(title, async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            if (await RequireWeekAsync(state, log, token).ConfigureAwait(false) is not int week)
            {
                return;
            }

            string name = await InScopeAsync(services => services.GetRequiredService<AppDbContext>().Memberships
                .AsNoTracking()
                .Where(membership => membership.Id == membershipId)
                .Select(membership => membership.DisplayNameOverride ?? membership.User!.DisplayName)
                .FirstAsync(token)).ConfigureAwait(false);

            await body(week, name, log, token).ConfigureAwait(false);
        }, cancellationToken);
}
