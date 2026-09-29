using NcaafPickEm.Infrastructure.Providers.Models;

namespace NcaafPickEm.Infrastructure.Providers.Simulator;

/// <summary>
/// The reference-data provider when <c>Providers:ReferenceData</c> is <c>Simulator</c> (P11-01,
/// D-183): there is no provider, because <c>tools/NcaafPickEm.Simulator</c> writes teams, the
/// calendar, the schedule, rankings and lines straight into the database it shares with the app.
/// Every call refuses with an explanation.
/// </summary>
/// <remarks>
/// Refusing rather than answering "nothing" matters: an empty rankings payload deletes the week's
/// poll, and an empty schedule is either refused as a bad refresh or postpones games the simulator
/// just wrote. Throwing goes through <c>ReferenceDataIngestService.RunAsync</c>'s failure path,
/// which writes nothing but the reason, so a manual refresh from the data page reads "owned by the
/// simulator" instead of quietly damaging the simulated week. The scheduled refresh jobs are not
/// registered in this mode at all.
/// </remarks>
public sealed class SimulatorOwnedReferenceDataProvider : IReferenceDataProvider
{
    /// <summary>The reason every call gives.</summary>
    public const string OwnedBySimulatorMessage =
        "Reference data is owned by NcaafPickEm.Simulator in this mode (Providers:ReferenceData=Simulator); " +
        "change teams, the calendar, games, rankings and lines from the simulator.";

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderConference>> GetConferencesAsync(
        int season,
        CancellationToken cancellationToken = default) => Refuse<ProviderConference>();

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderTeam>> GetTeamsAsync(
        int season,
        CancellationToken cancellationToken = default) => Refuse<ProviderTeam>();

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderGame>> GetGamesAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default) => Refuse<ProviderGame>();

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderRanking>> GetRankingsAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default) => Refuse<ProviderRanking>();

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderLine>> GetLinesAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default) => Refuse<ProviderLine>();

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderCalendarWeek>> GetCalendarAsync(
        int season,
        CancellationToken cancellationToken = default) => Refuse<ProviderCalendarWeek>();

    private static Task<IReadOnlyList<T>> Refuse<T>() =>
        Task.FromException<IReadOnlyList<T>>(new InvalidOperationException(OwnedBySimulatorMessage));
}
