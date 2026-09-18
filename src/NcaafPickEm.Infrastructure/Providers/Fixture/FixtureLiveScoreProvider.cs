using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Fixtures;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers.Fixture;

/// <summary>
/// Live scores for the Week 7, 2026 sample week, read from
/// <c>tests/NcaafPickEm.Fixtures/Data/Week7_2026/scores/snapshot-{n}.json</c>. Which snapshot (1
/// through <see cref="FixtureSnapshotState.MaxSnapshot"/>) is served comes from
/// <see cref="FixtureSnapshotState"/>, advanced either by a test hook or by
/// <c>POST /api/admin/fixture/snapshot/{n}</c> in Development.
/// </summary>
public sealed class FixtureLiveScoreProvider : ILiveScoreProvider
{
    private const string FixtureRoot = "Week7_2026";

    private readonly FixtureSnapshotState _snapshotState;

    /// <summary>Creates the provider.</summary>
    public FixtureLiveScoreProvider(FixtureSnapshotState snapshotState)
    {
        _snapshotState = snapshotState;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LiveScoreUpdate>> GetScoresAsync(
        DateOnly easternDate,
        CancellationToken cancellationToken = default)
    {
        int snapshot = _snapshotState.Current;
        SnapshotFile file = FixtureLoader.Read<SnapshotFile>($"{FixtureRoot}/scores/snapshot-{snapshot}.json");

        IReadOnlyList<LiveScoreUpdate> result =
        [
            .. file.Data
                .Where(g => KickoffEasternDate(g.KickoffUtc) == easternDate)
                .Select(g => new LiveScoreUpdate(
                    g.SourceEventId,
                    g.KickoffUtc,
                    g.HomeName,
                    g.HomeAbbreviation,
                    g.HomeSourceTeamId,
                    g.HomeScore,
                    g.AwayName,
                    g.AwayAbbreviation,
                    g.AwaySourceTeamId,
                    g.AwayScore,
                    ParseStatus(g.Status),
                    g.RawStatusName,
                    g.Completed,
                    g.Period,
                    g.Clock,
                    g.Spread)),
        ];

        return Task.FromResult(result);
    }

    private static DateOnly KickoffEasternDate(DateTime kickoffUtc)
    {
        DateTimeOffset utc = new(DateTime.SpecifyKind(kickoffUtc, DateTimeKind.Utc));
        return DateOnly.FromDateTime(SeasonCalendar.ToEastern(utc).DateTime);
    }

    private static GameStatus ParseStatus(string raw) =>
        Enum.TryParse<GameStatus>(raw, ignoreCase: true, out GameStatus parsed)
            ? parsed
            : GameStatus.Scheduled;

    private sealed record SnapshotFile(DateTime SnapshotUtc, IReadOnlyList<RawScore> Data);

    private sealed record RawScore(
        string SourceEventId,
        DateTime KickoffUtc,
        string HomeName,
        string HomeAbbreviation,
        string? HomeSourceTeamId,
        int? HomeScore,
        string AwayName,
        string AwayAbbreviation,
        string? AwaySourceTeamId,
        int? AwayScore,
        string Status,
        string RawStatusName,
        bool Completed,
        byte? Period,
        string? Clock,
        decimal? Spread);
}
