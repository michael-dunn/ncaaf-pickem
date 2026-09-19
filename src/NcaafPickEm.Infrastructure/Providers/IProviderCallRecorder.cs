using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>
/// Wraps every outbound provider call so it lands in <c>ProviderCalls</c> (Feature 09
/// observability, Feature 12 quota accounting). Success or failure, one row per call.
/// </summary>
public interface IProviderCallRecorder
{
    /// <summary>
    /// Runs <paramref name="call"/>, times it, and records the outcome. Exceptions are recorded
    /// and rethrown: the recorder observes, it never swallows.
    /// </summary>
    /// <param name="provider">Which provider is being called.</param>
    /// <param name="operation">The provider method, e.g. <c>"GetScoreboard"</c>.</param>
    /// <param name="call">The call itself.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="T">The call's result.</typeparam>
    Task<T> RecordAsync<T>(
        ProviderSource provider,
        string operation,
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many calls to <paramref name="provider"/> were recorded in the current UTC calendar
    /// month, for the free-tier quota counter on the data status page (Feature 12).
    /// </summary>
    /// <param name="provider">Which provider to count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountThisMonthAsync(ProviderSource provider, CancellationToken cancellationToken = default);
}
