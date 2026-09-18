using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Shared.Contracts.Invites;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Real <see cref="ILeaguesApi"/>, calling the routes in 03-API-Contracts.md over the shared
/// <see cref="HttpClient"/> (CSRF header and 401 redirect handlers already attached, see
/// <see cref="DependencyInjection"/>).
/// </summary>
/// <param name="httpClient">The app's scoped API client.</param>
public sealed class LeaguesApi(HttpClient httpClient) : ILeaguesApi
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<LeagueSummary[]> GetMyLeaguesAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync("api/leagues", cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadAsync<LeagueSummary[]>(response) ?? [];
    }

    /// <inheritdoc />
    public async Task<LeagueDetail> CreateLeagueAsync(
        CreateLeagueRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.PostAsJsonAsync("api/leagues", request, cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadRequiredAsync<LeagueDetail>(response);
    }

    /// <inheritdoc />
    public async Task<LeagueDetail> GetLeagueAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}", cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadRequiredAsync<LeagueDetail>(response);
    }

    /// <inheritdoc />
    public async Task<LeagueDetail> UpdateSettingsAsync(
        Guid leagueId,
        UpdateLeagueSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.PutAsJsonAsync($"api/leagues/{leagueId}/settings", request, cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadRequiredAsync<LeagueDetail>(response);
    }

    /// <inheritdoc />
    public async Task<MemberRow[]> GetMembersAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}/members", cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadAsync<MemberRow[]>(response) ?? [];
    }

    /// <inheritdoc />
    public async Task<MemberRow> SetMyDisplayNameAsync(
        Guid leagueId,
        SetLeagueDisplayNameRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
            $"api/leagues/{leagueId}/members/me/display-name", request, cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadRequiredAsync<MemberRow>(response);
    }

    /// <inheritdoc />
    public async Task RemoveMemberAsync(
        Guid leagueId,
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.DeleteAsync($"api/leagues/{leagueId}/members/{membershipId}", cancellationToken);
        await EnsureSuccessAsync(response);
    }

    /// <inheritdoc />
    public async Task PromoteMemberAsync(
        Guid leagueId,
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(
            $"api/leagues/{leagueId}/members/{membershipId}/promote", content: null, cancellationToken);
        await EnsureSuccessAsync(response);
    }

    /// <inheritdoc />
    public async Task DemoteMemberAsync(
        Guid leagueId,
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(
            $"api/leagues/{leagueId}/members/{membershipId}/demote", content: null, cancellationToken);
        await EnsureSuccessAsync(response);
    }

    /// <inheritdoc />
    public async Task TransferCommissionerAsync(
        Guid leagueId,
        Guid toMembershipId,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"api/leagues/{leagueId}/commissioner/transfer",
            new TransferRequest(toMembershipId),
            cancellationToken);
        await EnsureSuccessAsync(response);
    }

    /// <inheritdoc />
    public async Task<InviteResponse> CreateInviteAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(
            $"api/leagues/{leagueId}/invites", content: null, cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadRequiredAsync<InviteResponse>(response);
    }

    /// <inheritdoc />
    public async Task<InviteResponse[]> GetInvitesAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}/invites", cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadAsync<InviteResponse[]>(response) ?? [];
    }

    /// <inheritdoc />
    public async Task RevokeInviteAsync(
        Guid leagueId,
        Guid inviteId,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.DeleteAsync($"api/leagues/{leagueId}/invites/{inviteId}", cancellationToken);
        await EnsureSuccessAsync(response);
    }

    /// <inheritdoc />
    public async Task<InvitePreview> GetInvitePreviewAsync(string code, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/invites/{code}", cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadRequiredAsync<InvitePreview>(response);
    }

    /// <inheritdoc />
    public async Task<InviteAcceptOutcome> AcceptInviteAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.PostAsync($"api/invites/{code}/accept", content: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            InvitePreview? preview = await ReadAsync<InvitePreview>(response);
            return new InviteAcceptOutcome(League: null, Preview: preview);
        }

        await EnsureSuccessAsync(response);
        LeagueDetail league = await ReadRequiredAsync<LeagueDetail>(response);
        return new InviteAcceptOutcome(League: league, Preview: null);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>();

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response)
    {
        T? value = await ReadAsync<T>(response);
        return value ?? throw new LeaguesApiException((int)response.StatusCode, "The server sent an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ProblemDetailsBody? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>();
        }
        catch (System.Text.Json.JsonException)
        {
            // Not every failure response is a ProblemDetails body (e.g. a 401 with no content).
        }

        string message = problem?.Title
            ?? problem?.Detail
            ?? $"Request failed with status {(int)response.StatusCode}.";

        throw new LeaguesApiException((int)response.StatusCode, message, problem?.Errors);
    }
}
