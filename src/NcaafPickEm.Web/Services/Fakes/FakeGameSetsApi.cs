using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Reference;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// In-memory <see cref="IGameSetsApi"/> for Development, so P3-05's pages can be built and
/// screenshotted before P3-03's endpoints exist. Backed by <see cref="FakeGameSetStore"/>, seeded
/// from the "Week 7, 2026" fixture. See DECISIONS.md for the fake-API switch.
/// </summary>
/// <param name="store">Shared in-memory game set state.</param>
public sealed class FakeGameSetsApi(FakeGameSetStore store) : IGameSetsApi
{
    private readonly FakeGameSetStore _store = store;

    /// <inheritdoc />
    public Task<GameSetRuleDto[]> GetDefaultRulesAsync(Guid leagueId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.DefaultRules.ToArray());

    /// <inheritdoc />
    public Task<GameSetRuleDto[]> SaveDefaultRulesAsync(
        Guid leagueId, GameSetRuleDto[] rules, CancellationToken cancellationToken = default)
    {
        _store.DefaultRules.Clear();
        _store.DefaultRules.AddRange(rules);
        return Task.FromResult(rules);
    }

    /// <inheritdoc />
    public Task<WeekRulesResponse> GetWeekRulesAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.GetWeekRules(week));

    /// <inheritdoc />
    public Task<WeekRulesResponse> SaveWeekRulesAsync(
        Guid leagueId, int week, WeekRulesResponse request, CancellationToken cancellationToken = default)
    {
        if (_store.IsLocked && week == FakeGameSetStore.Week)
        {
            throw new LeaguesApiException(409, "This week is locked.");
        }

        _store.SetWeekRules(week, request);
        return Task.FromResult(request);
    }

    /// <inheritdoc />
    public Task<GameSetPreview> PreviewAsync(
        Guid leagueId, int week, GameSetRuleDto[] candidateRules, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Preview(candidateRules));

    /// <inheritdoc />
    public Task<WeekGameSetResponse> GenerateAsync(Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        RequireUnlocked(week);
        GameSetRuleDto[] rules = _store.GetWeekRules(week).Rules;
        GameSetPreview preview = _store.Preview(rules);
        if (preview.ExceedsMax)
        {
            throw new LeaguesApiException(409, "This selection has more than 50 games. Narrow the rules and try again.");
        }

        _store.Generate(rules);
        return Task.FromResult(ToWeekResponse(week));
    }

    /// <inheritdoc />
    public Task<WeekGameSetResponse> AddGameAsync(
        Guid leagueId, int week, Guid gameId, CancellationToken cancellationToken = default)
    {
        RequireUnlocked(week);
        _store.AddGame(gameId);
        return Task.FromResult(ToWeekResponse(week));
    }

    /// <inheritdoc />
    public Task<WeekGameSetResponse> RemoveGameAsync(
        Guid leagueId, int week, Guid gameId, CancellationToken cancellationToken = default)
    {
        RequireUnlocked(week);
        _store.RemoveGame(gameId);
        return Task.FromResult(ToWeekResponse(week));
    }

    /// <inheritdoc />
    public Task<WeekGameSetResponse> GetWeekGameSetAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default) =>
        Task.FromResult(ToWeekResponse(week));

    /// <inheritdoc />
    public Task<GameCandidate[]> SearchCandidatesAsync(
        int seasonYear, int week, string? search, CancellationToken cancellationToken = default)
    {
        IEnumerable<FakeGameSetStore.FakeGame> candidates = _store.AllCandidates;
        if (!string.IsNullOrWhiteSpace(search))
        {
            candidates = candidates.Where(g =>
                g.Home.School.Contains(search, StringComparison.OrdinalIgnoreCase)
                || g.Away.School.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        GameCandidate[] result =
        [
            .. candidates
                .OrderBy(g => g.KickoffUtc)
                .Select(g => new GameCandidate(
                    g.GameId,
                    g.Home,
                    g.Away,
                    g.HomeRank,
                    g.AwayRank,
                    g.KickoffUtc,
                    IsConferenceGame: g.Home.ConferenceId is not null && g.Home.ConferenceId == g.Away.ConferenceId)),
        ];
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<ConferenceDto[]> GetConferencesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Conferences);

    /// <inheritdoc />
    public Task<TeamDto[]> SearchTeamsAsync(string? search, CancellationToken cancellationToken = default)
    {
        TeamDto[] result = string.IsNullOrWhiteSpace(search)
            ? _store.Teams
            : [.. _store.Teams.Where(t => t.School.Contains(search, StringComparison.OrdinalIgnoreCase))];
        return Task.FromResult(result);
    }

    private void RequireUnlocked(int week)
    {
        if (_store.IsLocked && week == FakeGameSetStore.Week)
        {
            throw new LeaguesApiException(409, "This week is locked.");
        }
    }

    private WeekGameSetResponse ToWeekResponse(int week)
    {
        GameSetGameDto[] games = [.. _store.Games.Select(g => g.ToDto())];
        bool isLocked = _store.IsLocked && week == FakeGameSetStore.Week;
        DateTimeOffset? lockAt = games.Length == 0 ? null : games.Min(g => g.KickoffUtc);
        return new WeekGameSetResponse(
            week,
            lockAt,
            lockAt is null ? null : "Sat 12:00 PM ET",
            isLocked,
            IsComplete: false,
            games);
    }
}
