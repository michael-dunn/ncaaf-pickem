using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Contracts.Reference;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// In-memory <see cref="IPicksApi"/> for Development, so P4-03's page can be built and
/// screenshotted for every state before running against the real (already-merged) P4-01
/// endpoints. Backed by <see cref="FakeGameSetStore"/>, so a pick made here is the same "Week 7,
/// 2026" set the game-set pages show. <see cref="PastWeek"/> is a second, fixed, read-only week
/// with results, for the past-week screenshot. See DECISIONS.md for the fake-API switch.
/// </summary>
/// <param name="store">Shared in-memory game set state.</param>
public sealed class FakePicksApi(FakeGameSetStore store) : IPicksApi
{
    /// <summary>A week before <see cref="FakeGameSetStore.Week"/>, fixed with three Final games so the
    /// past-week screenshot has correct/incorrect coloring to show.</summary>
    public const int PastWeek = 6;

    private readonly FakeGameSetStore _store = store;

    /// <inheritdoc />
    public Task<MyPicksResponse> GetMyPicksAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default) =>
        Task.FromResult(ToResponse(week));

    /// <inheritdoc />
    public Task<MyPicksResponse> SetPickAsync(
        Guid leagueId, int week, Guid gameId, Guid teamId, CancellationToken cancellationToken = default)
    {
        RequirePickable(week);

        if (_store.FailNextPick)
        {
            _store.FailNextPick = false;
            throw new LeaguesApiException(0, "Offline");
        }

        _store.SetMyPick(gameId, teamId);
        return Task.FromResult(ToResponse(week));
    }

    /// <inheritdoc />
    public Task<MyPicksResponse> SubmitAsync(Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        RequirePickable(week);
        FakeGameSetStore.FakeGame[] games = [.. _store.Games];
        int missing = games.Count(g => !_store.MyPicks.ContainsKey(g.GameId));
        if (missing > 0)
        {
            throw new LeaguesApiException(409, "IncompletePicks", count: missing);
        }

        _store.MarkSubmitted(DateTimeOffset.UtcNow);
        return Task.FromResult(ToResponse(week));
    }

    /// <inheritdoc />
    public Task AckChangesAsync(Guid leagueId, int week, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private void RequirePickable(int week)
    {
        if (week != FakeGameSetStore.Week)
        {
            throw new LeaguesApiException(409, "WeekNotCurrent");
        }

        if (_store.IsLocked)
        {
            throw new LeaguesApiException(409, "Locked");
        }
    }

    private MyPicksResponse ToResponse(int week) => week == PastWeek ? PastWeekResponse() : CurrentWeekResponse(week);

    private MyPicksResponse PastWeekResponse()
    {
        TeamDto Find(string abbr) => _store.Teams.First(t => t.Abbreviation == abbr);

        GameSetGameDto Final(TeamDto home, int? homeRank, TeamDto away, int? awayRank, int homeScore, int awayScore, string kickoffUtc, decimal? spread)
        {
            Guid gameId = Guid.NewGuid();
            Guid winnerTeamId = homeScore > awayScore ? home.TeamId : away.TeamId;
            return new GameSetGameDto(
                gameId, gameId, home, away, homeRank, awayRank, DateTimeOffset.Parse(kickoffUtc),
                PointValue: 10, IsPointValueElevated: false, GameSetGameSource.Rule, GameStatus.Final,
                homeScore, awayScore, Period: null, Clock: null, IsVoided: false, winnerTeamId, spread);
        }

        GameSetGameDto michiganTexas = Final(Find("MICH"), 8, Find("TEX"), 5, 24, 17, "2026-10-10T19:30:00Z", spread: -6.5m);
        GameSetGameDto bamaAuburn = Final(Find("ALA"), 11, Find("AUB"), null, 20, 27, "2026-10-10T19:30:00Z", spread: -3m);
        GameSetGameDto georgiaKentucky = Final(Find("UGA"), 2, Find("UK"), null, 35, 10, "2026-10-10T16:00:00Z", spread: -17.5m);

        MyPickGameDto[] games =
        [
            .. new[]
            {
                new MyPickGameDto(michiganTexas, michiganTexas.HomeTeam.TeamId, false), // picked Michigan: correct
                new MyPickGameDto(bamaAuburn, bamaAuburn.HomeTeam.TeamId, false), // picked Alabama: Auburn won
                new MyPickGameDto(georgiaKentucky, georgiaKentucky.AwayTeam.TeamId, false), // picked Kentucky: Georgia won
            }.OrderBy(g => g.Game.KickoffUtc),
        ];

        return new MyPicksResponse(
            PastWeek,
            SubmissionStatus.Locked,
            games.Min(g => g.Game.KickoffUtc),
            "Sat 12:00 PM ET",
            IsLocked: true,
            PickedCount: 3,
            TotalCount: 3,
            HasUnseenGameChanges: false,
            games);
    }

    private MyPicksResponse CurrentWeekResponse(int week)
    {
        bool isCurrentWeek = week == FakeGameSetStore.Week;
        FakeGameSetStore.FakeGame[] games = isCurrentWeek ? [.. _store.Games] : [];
        bool isLocked = _store.IsLocked && isCurrentWeek;

        int totalCount = games.Length;
        int pickedCount = games.Count(g => _store.MyPicks.ContainsKey(g.GameId));
        bool submitted = _store.SubmittedUtc is not null && pickedCount == totalCount && totalCount > 0;

        SubmissionStatus status = isLocked
            ? (submitted ? SubmissionStatus.Locked : SubmissionStatus.Incomplete)
            : submitted
                ? SubmissionStatus.Submitted
                : pickedCount == 0
                    ? SubmissionStatus.NotStarted
                    : SubmissionStatus.InProgress;

        DateTimeOffset? lockAt = totalCount == 0 ? null : games.Min(g => g.KickoffUtc);

        MyPickGameDto[] myGames =
        [
            .. games
                .OrderBy(g => g.KickoffUtc)
                .Select(g => new MyPickGameDto(
                    Game: g.ToDto(),
                    MyTeamId: _store.MyPicks.TryGetValue(g.GameId, out Guid teamId) ? teamId : null,
                    IsNewSinceSubmit: false)),
        ];

        return new MyPicksResponse(
            week,
            status,
            lockAt,
            lockAt is null ? null : "Sat 12:00 PM ET",
            isLocked,
            pickedCount,
            totalCount,
            HasUnseenGameChanges: false,
            myGames);
    }
}
