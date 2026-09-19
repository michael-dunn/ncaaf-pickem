using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>
/// The only <see cref="IProviderCallRecorder"/>. Writes one <c>ProviderCalls</c> row per outbound
/// call in a scope of its own, so the record survives whatever happens to the caller's unit of
/// work, and so a singleton caller (the Saturday poller) can use it.
/// </summary>
/// <remarks>
/// A failure to write the audit row is logged and swallowed: losing a call record must never turn
/// a working provider call into a failed one.
/// </remarks>
public sealed class ProviderCallRecorder : IProviderCallRecorder
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProviderCallRecorder> _logger;

    /// <summary>Creates the recorder.</summary>
    /// <param name="scopeFactory">Used to open a short scope for the audit write.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Logger.</param>
    public ProviderCallRecorder(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<ProviderCallRecorder> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<T> RecordAsync<T>(
        ProviderSource provider,
        string operation,
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        DateTime startedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        long startedTicks = Stopwatch.GetTimestamp();

        try
        {
            T result = await call(cancellationToken).ConfigureAwait(false);
            await WriteAsync(provider, operation, startedUtc, startedTicks, true, null, null, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Provider call {Provider}.{Operation} succeeded in {DurationMs} ms",
                provider,
                operation,
                Elapsed(startedTicks));
            return result;
        }
        catch (Exception ex)
        {
            int? statusCode = TryGetStatusCode(ex);
            await WriteAsync(provider, operation, startedUtc, startedTicks, false, statusCode, ex.Message, CancellationToken.None)
                .ConfigureAwait(false);
            _logger.LogWarning(
                ex,
                "Provider call {Provider}.{Operation} failed after {DurationMs} ms",
                provider,
                operation,
                Elapsed(startedTicks));
            throw;
        }
    }

    /// <summary>
    /// Count of calls to <paramref name="provider"/> recorded so far in the current UTC calendar
    /// month (P2-02), for the free-tier usage counter on the data status page.
    /// </summary>
    public async Task<int> CountThisMonthAsync(ProviderSource provider, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTime monthStartUtc = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        string providerName = provider.ToString();

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await database.ProviderCalls
            .AsNoTracking()
            .Where(call => call.Provider == providerName && call.StartedUtc >= monthStartUtc)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static int? TryGetStatusCode(Exception ex)
    {
        if (ex is HttpRequestException { StatusCode: HttpStatusCode code })
        {
            return (int)code;
        }

        // Kiota's ApiException (the CFBD client, P2-02) carries the HTTP status on every
        // request-shaped failure; 0 is its own "unknown" default.
        if (ex is ApiException { ResponseStatusCode: not 0 } apiException)
        {
            return apiException.ResponseStatusCode;
        }

        return null;
    }

    private static int Elapsed(long startedTicks) =>
        (int)Math.Min(Stopwatch.GetElapsedTime(startedTicks).TotalMilliseconds, int.MaxValue);

    private async Task WriteAsync(
        ProviderSource provider,
        string operation,
        DateTime startedUtc,
        long startedTicks,
        bool success,
        int? statusCode,
        string? error,
        CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            database.ProviderCalls.Add(new ProviderCall
            {
                Id = Guid.CreateVersion7(),
                Provider = provider.ToString(),
                Operation = Truncate(operation, ProviderCall.OperationMaxLength),
                StartedUtc = startedUtc,
                DurationMs = Elapsed(startedTicks),
                Success = success,
                StatusCode = statusCode,
                Error = error is null ? null : Truncate(error, ProviderCall.ErrorMaxLength),
            });

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not record the {Provider}.{Operation} call", provider, operation);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
