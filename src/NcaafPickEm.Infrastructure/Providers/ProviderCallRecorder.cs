using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Kiota.Abstractions;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>
/// Wraps one outbound call to an external provider and writes exactly one <c>ProviderCalls</c>
/// row for it (Feature 12): started instant, duration, success, HTTP status when known, and the
/// error message on failure. Every <c>CfbdReferenceDataProvider</c> call goes through
/// this; P2-03's ESPN provider reuses it, which is why the API stays this small.
/// </summary>
/// <remarks>
/// Scoped, because it shares the caller's <see cref="AppDbContext"/> — the recorded row commits
/// on its own (via <c>SaveChangesAsync</c>) regardless of whether the
/// caller's own unit of work later succeeds or fails, since a failed provider call should still
/// show up in the log even if the ingest that triggered it rolls back.
/// </remarks>
public sealed class ProviderCallRecorder
{
    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the recorder.</summary>
    public ProviderCallRecorder(AppDbContext database, TimeProvider timeProvider)
    {
        _database = database;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Runs <paramref name="call"/>, writes one <c>ProviderCalls</c> row for it, then returns its
    /// result. Re-throws whatever <paramref name="call"/> throws after the row is written, so a
    /// failure is always logged before the caller sees the exception.
    /// </summary>
    /// <param name="provider">Which external provider this call goes to.</param>
    /// <param name="operation">The provider method called, e.g. "GetGames".</param>
    /// <param name="call">The outbound call. Given the token this method was called with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<T> RecordAsync<T>(
        ProviderSource provider,
        string operation,
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        DateTime startedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var stopwatch = Stopwatch.StartNew();
        bool success = false;
        int? statusCode = null;
        string? error = null;

        try
        {
            T result = await call(cancellationToken).ConfigureAwait(false);
            success = true;
            return result;
        }
        catch (Exception ex)
        {
            statusCode = TryGetStatusCode(ex);
            error = Truncate(ex.Message, ProviderCall.ErrorMaxLength);
            throw;
        }
        finally
        {
            stopwatch.Stop();

            _database.ProviderCalls.Add(new ProviderCall
            {
                Id = Guid.CreateVersion7(),
                Provider = provider.ToString(),
                Operation = Truncate(operation, ProviderCall.OperationMaxLength),
                StartedUtc = startedUtc,
                DurationMs = checked((int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue)),
                Success = success,
                StatusCode = statusCode,
                Error = error,
            });

            await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Count of calls to <paramref name="provider"/> recorded so far in the current UTC calendar
    /// month, for the free-tier usage counter on the data status page.
    /// </summary>
    public Task<int> CountThisMonthAsync(ProviderSource provider, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTime monthStartUtc = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        string providerName = provider.ToString();

        return _database.ProviderCalls
            .Where(call => call.Provider == providerName && call.StartedUtc >= monthStartUtc)
            .CountAsync(cancellationToken);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static int? TryGetStatusCode(Exception ex) =>
        // Kiota's ApiException carries the HTTP status on every request-shaped failure; anything
        // else (a timeout, DNS failure, ...) has no status code to report. 0 is ApiException's
        // own "unknown" default, so it is treated the same as "no status".
        ex is ApiException { ResponseStatusCode: not 0 } apiException
            ? apiException.ResponseStatusCode
            : null;
}
