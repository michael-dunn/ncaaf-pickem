using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Shared.Contracts.Auth;

/// <summary>
/// Body of <c>GET /api/me</c> and <c>PUT /api/me</c>. Anonymous callers get 401, so a body here
/// always means "signed in".
/// </summary>
/// <param name="UserId">Our <c>Users.Id</c>.</param>
/// <param name="Email">The Tailscale login (an email address for most identity providers).</param>
/// <param name="DisplayName">Account-wide display name, 1..30 characters.</param>
/// <param name="Leagues">Every league the caller is an active member of.</param>
public sealed record MeResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    LeagueSummary[] Leagues);
