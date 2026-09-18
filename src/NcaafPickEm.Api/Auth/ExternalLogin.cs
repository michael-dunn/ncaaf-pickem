namespace NcaafPickEm.Api.Auth;

/// <summary>
/// What we take from Google's ticket. Nothing else about the external principal is kept.
/// </summary>
/// <param name="Subject">Google's stable <c>sub</c> claim; the match key on a returning login.</param>
/// <param name="Email">The Google account email.</param>
/// <param name="Name">The Google profile name, used only as the initial display name.</param>
public sealed record ExternalLogin(string Subject, string Email, string? Name);
