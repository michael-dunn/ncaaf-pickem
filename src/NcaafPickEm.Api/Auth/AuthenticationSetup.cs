using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace NcaafPickEm.Api.Auth;

/// <summary>
/// The scheme set: a policy scheme that picks between Tailscale identity headers and the
/// session cookie, plus the two schemes it forwards to.
/// </summary>
/// <remarks>
/// <see cref="AuthDefaults.SelectorScheme"/> is the default authenticate scheme. It forwards to
/// <see cref="AuthDefaults.TailscaleScheme"/> whenever the request carries a non-empty
/// <see cref="AuthDefaults.TailscaleLoginHeader"/> - which, behind <c>tailscale serve</c>, is
/// every request from a tailnet user - and to the cookie otherwise. The challenge scheme stays
/// the cookie, because it is the cookie that owns the 401/403-under-<c>/api</c> behaviour the
/// SPA depends on; header identity has nothing to challenge for. Since P9-03 the cookie is
/// written by <c>/auth/dev-login</c> alone (Development and Testing), so in Production the
/// selector always lands on the Tailscale scheme or on nobody.
/// </remarks>
public static class AuthenticationSetup
{
    /// <summary>Registers the three schemes and the three policies.</summary>
    public static IServiceCollection AddAppAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ExternalSignInService>();

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = AuthDefaults.SelectorScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddPolicyScheme(AuthDefaults.SelectorScheme, AuthDefaults.SelectorScheme, ConfigureSelector)
            .AddScheme<AuthenticationSchemeOptions, TailscaleAuthenticationHandler>(
                AuthDefaults.TailscaleScheme,
                _ => { })
            .AddCookie(ConfigureCookie);

        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyNames.Authenticated, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(PolicyNames.LeagueMember, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(PolicyNames.LeagueCommissioner, policy => policy.RequireAuthenticatedUser());

        return services;
    }

    private static void ConfigureSelector(PolicySchemeOptions options)
    {
        // Sign-in forwards through the same selector, so /auth/dev-login (which names the cookie
        // scheme explicitly) is unaffected either way.
        options.ForwardDefaultSelector = static context =>
            string.IsNullOrWhiteSpace(context.Request.Headers[AuthDefaults.TailscaleLoginHeader].ToString())
                ? CookieAuthenticationDefaults.AuthenticationScheme
                : AuthDefaults.TailscaleScheme;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = AuthDefaults.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;

        // Feature 08 asked for a whole season without re-authenticating. Sliding, so an active
        // dev session never expires mid-use.
        options.ExpireTimeSpan = AuthDefaults.SessionLifetime;
        options.SlidingExpiration = true;

        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";

        // The SPA fetches /api itself, so a 302 to a login page there would be parsed as JSON and
        // swallowed. Under /api, say 401 and 403 and let the client decide to navigate.
        options.Events.OnRedirectToLogin = context => WriteStatus(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => WriteStatus(context, StatusCodes.Status403Forbidden);
    }

    private static Task WriteStatus(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            // UseStatusCodePages + AddProblemDetails turn the bare status into ProblemDetails JSON.
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}
