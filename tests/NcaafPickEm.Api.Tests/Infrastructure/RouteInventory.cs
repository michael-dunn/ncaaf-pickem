using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Auth;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// One mapped endpoint reduced to the security facts P8-01 asserts on.
/// </summary>
/// <param name="Pattern">Route template without its leading or trailing slash, constraints kept.</param>
/// <param name="Methods">Comma-joined HTTP methods.</param>
/// <param name="Policy">Every authorization policy on the endpoint, joined with <c>+</c>.</param>
/// <param name="AllowsAnonymous">True when <c>AllowAnonymous()</c> was applied.</param>
/// <param name="Scope">The scoping helper used, from <see cref="EndpointScopeMetadata"/>.</param>
/// <param name="IsCsrfProtected">True when <c>RequireCsrfHeader()</c> covers the endpoint.</param>
public sealed record RouteFact(
    string Pattern,
    string Methods,
    string? Policy,
    bool AllowsAnonymous,
    EndpointScope? Scope,
    bool IsCsrfProtected)
{
    private static readonly string[] MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>Route prefixes the app owns; anything else is a static asset or the SPA fallback.</summary>
    public static readonly string[] ApplicationPrefixes = ["api/", "auth/", "health"];

    /// <summary>True for a route under <c>/api</c>.</summary>
    public bool IsApi => Pattern.StartsWith("api/", StringComparison.Ordinal);

    /// <summary>True for a route the app maps itself, as opposed to a static asset or the fallback.</summary>
    public bool IsApplicationRoute =>
        ApplicationPrefixes.Any(prefix => Pattern.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>True when any of <see cref="Methods"/> mutates.</summary>
    public bool IsMutation => MutatingMethods.Any(method => Methods.Contains(method, StringComparison.Ordinal));

    /// <summary>The first HTTP method, for probing the route.</summary>
    public HttpMethod PrimaryMethod => new(Methods.Split(',')[0]);

    /// <summary>
    /// "METHOD /route" with route constraints stripped, i.e. exactly how the row reads in
    /// 03-API-Contracts.md.
    /// </summary>
    public string Key => $"{Methods} /{StripConstraints(Pattern)}";

    /// <summary>The inventory line for the P8-01 report.</summary>
    public string Describe() =>
        $"{Methods} | /{Pattern} | {(string.IsNullOrEmpty(Policy) ? "(none)" : Policy)} "
        + $"| {Scope?.ToString() ?? "-"} | {(IsCsrfProtected ? "yes" : "no")}";

    /// <summary>
    /// Fills every route parameter with a syntactically valid value, so a route can be probed for
    /// its authorization answer without reaching a handler.
    /// </summary>
    /// <param name="leagueId">The seeded league to substitute for <c>{leagueId}</c>.</param>
    public string ToRequestPath(Guid leagueId)
    {
        IEnumerable<string> segments = Pattern.Split('/').Select(segment =>
        {
            if (!segment.StartsWith('{'))
            {
                return segment;
            }

            string name = segment.Trim('{', '}').Split(':')[0];

            return name switch
            {
                "leagueId" => leagueId.ToString(),
                "week" => "7",
                "year" => "2026",
                "n" => "1",
                "code" => "ABCDEFGH",
                "dataType" => "Teams",
                _ when segment.Contains(":guid", StringComparison.Ordinal) => Guid.CreateVersion7().ToString(),
                _ when segment.Contains(":int", StringComparison.Ordinal) => "1",
                _ => "placeholder",
            };
        });

        return "/" + string.Join('/', segments);
    }

    /// <summary>Reads every mapped endpoint out of a booted app.</summary>
    public static RouteFact[] From(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return
        [
            .. services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Select(FromEndpoint)
                .OrderBy(route => route.Pattern, StringComparer.Ordinal)
                .ThenBy(route => route.Methods, StringComparer.Ordinal)
        ];
    }

    private static RouteFact FromEndpoint(RouteEndpoint endpoint)
    {
        IReadOnlyList<string> methods =
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];

        // Every policy in play, group-level first. All three assert only "signed in"
        // (PolicyNames), so the league scope itself is the EndpointScopeMetadata below.
        string policy = string.Join(
            "+",
            endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Select(data => data.Policy)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct(StringComparer.Ordinal));

        // RawText keeps its leading slash, and a group mapped with "" leaves a trailing one.
        string pattern = (endpoint.RoutePattern.RawText ?? string.Empty).Trim('/');

        return new RouteFact(
            pattern,
            string.Join(",", methods),
            policy,
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null,
            endpoint.Metadata.GetMetadata<EndpointScopeMetadata>()?.Scope,
            endpoint.Metadata.GetMetadata<CsrfProtectedMetadata>() is not null);
    }

    /// <summary>Turns <c>{leagueId:guid}</c> back into <c>{leagueId}</c>.</summary>
    private static string StripConstraints(string pattern) => string.Join(
        '/',
        pattern.Split('/').Select(segment =>
            segment.StartsWith('{') && segment.Contains(':', StringComparison.Ordinal)
                ? string.Concat(segment.AsSpan(0, segment.IndexOf(':', StringComparison.Ordinal)), "}")
                : segment));

    /// <summary>The whole inventory as a table, for the P8-01 report and test output.</summary>
    public static string Render(IEnumerable<RouteFact> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        IEnumerable<string> lines = routes
            .Where(route => route.IsApplicationRoute)
            .Select(route => route.Describe());

        return string.Join(
            Environment.NewLine,
            ["METHOD(S) | ROUTE | POLICY | SCOPE | CSRF", .. lines])
            .ToString(CultureInfo.InvariantCulture);
    }
}
