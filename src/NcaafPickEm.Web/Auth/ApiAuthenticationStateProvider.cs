using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using NcaafPickEm.Shared.Contracts.Auth;

namespace NcaafPickEm.Web.Auth;

/// <summary>
/// Answers "who is signed in?" by asking <c>GET /api/me</c>.
/// </summary>
/// <remarks>
/// The session lives in an <c>HttpOnly</c> cookie the WASM app cannot read, so the server is the
/// only source of truth. The answer is cached for the lifetime of the app instance; a full page
/// load after sign-in or sign-out is what refreshes it, and <see cref="Invalidate"/> forces it.
/// </remarks>
public sealed class ApiAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly HttpClient _httpClient;
    private Task<AuthenticationState>? _cached;

    /// <summary>Creates the provider.</summary>
    public ApiAuthenticationStateProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>The signed-in account, or null when anonymous. Populated by the last state read.</summary>
    public MeResponse? CurrentUser { get; private set; }

    /// <inheritdoc />
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _cached ??= LoadAsync();

    /// <summary>Drops the cache and re-reads <c>/api/me</c>, notifying every subscriber.</summary>
    public void Invalidate()
    {
        _cached = LoadAsync();
        NotifyAuthenticationStateChanged(_cached);
    }

    private async Task<AuthenticationState> LoadAsync()
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync("api/me");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                CurrentUser = null;
                return Anonymous;
            }

            response.EnsureSuccessStatusCode();

            MeResponse? me = await response.Content.ReadFromJsonAsync<MeResponse>();
            if (me is null)
            {
                CurrentUser = null;
                return Anonymous;
            }

            CurrentUser = me;

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, me.UserId.ToString()),
                    new Claim(ClaimTypes.Name, me.DisplayName),
                    new Claim(ClaimTypes.Email, me.Email),
                ],
                authenticationType: "NcaafPickEm");

            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch (HttpRequestException)
        {
            // Offline or the API is down. Treat it as signed out rather than crashing the shell;
            // the PWA still renders and the next navigation retries.
            CurrentUser = null;
            return Anonymous;
        }
    }
}
