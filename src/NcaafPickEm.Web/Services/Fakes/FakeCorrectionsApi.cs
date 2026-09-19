using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Scoring;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// In-memory <see cref="ICorrectionsApi"/> for Development, so P5-05's corrections UI and audit
/// page can be built and screenshotted before P5-02's server endpoints exist. Backed by
/// <see cref="FakeGameSetStore"/> so an override or void made here shows up in the same store the
/// week view and Data status page already read from. See DECISIONS.md for the fake-API switch.
/// </summary>
/// <param name="store">Shared in-memory game set state.</param>
public sealed class FakeCorrectionsApi(FakeGameSetStore store) : ICorrectionsApi
{
    private readonly FakeGameSetStore _store = store;

    /// <inheritdoc />
    public Task<GameSetGameDto> OverrideResultAsync(
        Guid leagueId, int week, Guid gameId, Guid winnerTeamId, string reason, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.OverrideResult(week, gameId, winnerTeamId, reason, "Michael"));

    /// <inheritdoc />
    public Task<GameSetGameDto> VoidGameAsync(
        Guid leagueId, int week, Guid gameId, string reason, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.VoidGame(week, gameId, reason, "Michael"));

    /// <inheritdoc />
    public Task<AuditEntry[]> GetAuditAsync(Guid leagueId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.AuditLog.ToArray());
}
