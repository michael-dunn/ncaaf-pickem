using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Points;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// In-memory <see cref="IPointRulesApi"/> for Development, backed by the same
/// <see cref="FakeGameSetStore"/> as <see cref="FakeGameSetsApi"/> so an override made on "This
/// week's games" is visible everywhere else. See DECISIONS.md for the fake-API switch.
/// </summary>
/// <param name="store">Shared in-memory game set state.</param>
public sealed class FakePointRulesApi(FakeGameSetStore store) : IPointRulesApi
{
    private readonly FakeGameSetStore _store = store;

    /// <inheritdoc />
    public Task<PointRuleDto[]> GetRulesAsync(Guid leagueId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.PointRules.OrderBy(r => r.Priority).ToArray());

    /// <inheritdoc />
    public Task<PointRuleDto[]> SaveRulesAsync(
        Guid leagueId, PointRuleDto[] rules, CancellationToken cancellationToken = default)
    {
        _store.PointRules.Clear();
        _store.PointRules.AddRange(rules.OrderBy(r => r.Priority));
        return Task.FromResult(rules);
    }

    /// <inheritdoc />
    public Task<WeekGameSetResponse> SetOverrideAsync(
        Guid leagueId, int week, Guid gameId, int? pointValue, CancellationToken cancellationToken = default)
    {
        if (_store.IsLocked && week == FakeGameSetStore.Week)
        {
            throw new LeaguesApiException(409, "This week is locked.");
        }

        _store.SetOverride(gameId, pointValue);

        GameSetGameDto[] games = [.. _store.Games.Select(g => g.ToDto())];
        DateTimeOffset? lockAt = games.Length == 0 ? null : games.Min(g => g.KickoffUtc);
        return Task.FromResult(new WeekGameSetResponse(
            week, lockAt, lockAt is null ? null : "Sat 12:00 PM ET", _store.IsLocked, IsComplete: false, games));
    }
}
