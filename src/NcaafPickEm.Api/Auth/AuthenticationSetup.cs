using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

namespace NcaafPickEm.Api.Auth;

/// <summary>
/// The scheme set: a policy scheme that picks between Tailscale identity headers and the
/// session cookie, plus the Google handler.
/// </summary>
/// <remarks>
/// <see cref="AuthDefaults.SelectorScheme"/> is the default authenticate scheme. It forwards to
/// <see cref="AuthDefaults.TailscaleScheme"/> whenever the request carries a non-empty
/// <see cref="AuthDefaults.TailscaleLoginHeader"/> - which, behind <c>tailscale serve</c>, is
/// every request from a tailnet user - and to the cookie otherwise. The challenge scheme stays
/// the cookie, because it is the cookie that owns the 401/403-under-<c>/api</c> behaviour the
/// SPA depends on; header identity has nothing to challenge for.
/// </remarks>
public static class AuthenticationSetup
{
    // Google's options validate that these are non-empty, so a machine with no OAuth client
    // configured (CI, a fixtures-only dev box, every API test) still has to hand it something.
    // Nothing can be signed in with these; a challenge would simply be rejected by Google.
    private const string PlaceholderClientId = "ncaaf-pickem-google-client-id-not-configured";
    private const string PlaceholderClientSecret = "ncaaf-pickem-google-client-secret-not-configured";

    /// <summary>Registers the four schemes and the three policies.</summary>
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
            .AddCookie(ConfigureCookie)
            .AddGoogle(options => ConfigureGoogle(options, configuration));

        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyNames.Authenticated, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(PolicyNames.LeagueMember, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(PolicyNames.LeagueCommissioner, policy => policy.RequireAuthenticatedUser());

        return services;
    }

    private static void ConfigureSelector(PolicySchemeOptions options)
    {
        // Sign-in and sign-out forward through the same selector, so /auth/dev-login (which names
        // the cookie scheme explicitly) is unaffected either way.
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

        // Feature 08: a whole season without re-authenticating. Sliding, so an active phone
        // never gets logged out.
        options.ExpireTimeSpan = AuthDefaults.SessionLifetime;
        options.SlidingExpiration = true;

        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
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

    private static void ConfigureGoogle(GoogleOptions options, IConfiguration configuration)
    {
        options.ClientId = configuration["Google:ClientId"] is { Length: > 0 } clientId
            ? clientId
            : PlaceholderClientId;

        options.ClientSecret = configuration["Google:ClientSecret"] is { Length: > 0 } clientSecret
            ? clientSecret
            : PlaceholderClientSecret;

        options.CallbackPath = AuthDefaults.GoogleCallbackPath;

        // Never reached: OnTicketReceived handles the response itself, before the handler would
        // sign the Google principal into anything. It still has to name a real scheme.
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        options.SaveTokens = false;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;

        options.Events.OnTicketReceived = OnGoogleTicketReceivedAsync;
    }

    private static async Task OnGoogleTicketReceivedAsync(TicketReceivedContext context)
    {
        ExternalLogin? login = ReadExternalLogin(context.Principal);

        // RemoteAuthenticationHandler moves the challenge's RedirectUri onto ReturnUri and nulls
        // out Properties.RedirectUri before raising this event, so ReturnUri is the only place the
        // caller's returnUrl still exists.
        string returnUrl = ReturnUrl.Sanitize(context.ReturnUri);

        if (login is null)
        {
            context.Fail("Google did not return a subject and an email.");
            return;
        }

        ExternalSignInService signIn = context.HttpContext.RequestServices
            .GetRequiredService<ExternalSignInService>();

        await signIn.SignInAsync(context.HttpContext, login, context.HttpContext.RequestAborted);

        // Handle the response ourselves so the handler does not also sign the Google principal
        // into SignInScheme and overwrite the cookie we just wrote.
        context.HandleResponse();
        context.Response.Redirect(returnUrl);
    }

    private static ExternalLogin? ReadExternalLogin(ClaimsPrincipal? principal)
    {
        string? subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        string? email = principal?.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new ExternalLogin(subject, email, principal?.FindFirstValue(ClaimTypes.Name));
    }
}
