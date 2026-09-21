using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace NcaafPickEm.Api.Auth;

/// <summary>
/// Fixed-window rate limits on the two anonymous-ish entry points (P8-01).
/// </summary>
/// <remarks>
/// Everything else under <c>/api</c> needs an identity and an active membership, so the cheapest
/// thing an unauthenticated caller can do is hammer <c>/auth/dev-login</c> (Development and
/// Testing only; nothing is mapped under <c>/auth</c> in Production) or guess invite codes.
/// The windows are deliberately generous: a real person redeems one invite, while a
/// code-guessing script needs orders of magnitude more attempts than this allows. Invite codes
/// are 8 characters from a 31-character alphabet, so 20 guesses a minute is nowhere near a
/// keyspace search.
/// </remarks>
public static class RateLimitingSetup
{
    /// <summary>Policy on <c>/auth/*</c>, which since P9-03 is <c>/auth/dev-login</c> alone.</summary>
    public const string AuthPolicy = "auth";

    /// <summary>Policy on <c>/api/invites/*</c> (preview and accept).</summary>
    public const string InvitePolicy = "invites";

    /// <summary>Configuration key that turns the limiter off (tests set it false).</summary>
    public const string EnabledKey = "RateLimiting:Enabled";

    /// <summary>Requests per minute per client IP on <c>/auth/*</c> when no key is configured.</summary>
    public const int DefaultAuthPermitPerMinute = 30;

    /// <summary>Requests per minute per client IP on <c>/api/invites/*</c> when no key is configured.</summary>
    public const int DefaultInvitePermitPerMinute = 20;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Registers the two fixed-window policies and the 429 <c>ProblemDetails</c> response.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">
    /// Reads <c>RateLimiting:Enabled</c> (default true), <c>RateLimiting:AuthPermitPerMinute</c>
    /// and <c>RateLimiting:InvitePermitPerMinute</c>.
    /// </param>
    public static IServiceCollection AddAppRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        bool enabled = configuration.GetValue<bool?>(EnabledKey) ?? true;

        int authPermit = configuration.GetValue<int?>("RateLimiting:AuthPermitPerMinute")
            ?? DefaultAuthPermitPerMinute;
        int invitePermit = configuration.GetValue<int?>("RateLimiting:InvitePermitPerMinute")
            ?? DefaultInvitePermitPerMinute;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteProblemDetailsAsync;

            options.AddPolicy(AuthPolicy, context => Partition(context, enabled, AuthPolicy, authPermit));
            options.AddPolicy(InvitePolicy, context => Partition(context, enabled, InvitePolicy, invitePermit));
        });

        return services;
    }

    /// <summary>
    /// One fixed window per client IP per policy. A request with no resolvable remote address
    /// (a unix socket, or the in-memory test server) shares the "unknown" partition rather than
    /// escaping the limit.
    /// </summary>
    private static RateLimitPartition<string> Partition(
        HttpContext context,
        bool enabled,
        string policy,
        int permitLimit)
    {
        if (!enabled)
        {
            return RateLimitPartition.GetNoLimiter($"{policy}:disabled");
        }

        string client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{policy}:{client}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = Window,

                // No queueing: a refused caller should be told to come back, not held open.
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
    }

    private static ValueTask WriteProblemDetailsAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        HttpResponse response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        // UseStatusCodePages + AddProblemDetails turn the bare status into ProblemDetails JSON,
        // exactly as they do for the 401 and 403 the cookie handler writes.
        return ValueTask.CompletedTask;
    }
}
