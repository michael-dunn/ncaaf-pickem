namespace NcaafPickEm.Api.Auth;

/// <summary>
/// The three things we take from an external identity, whatever produced it: a Tailscale
/// identity header (Phase 9) or a Google ticket. Nothing else about the external principal is
/// kept.
/// </summary>
/// <param name="Subject">
/// The provider's stable handle for the person; the match key on every returning request. Under
/// Tailscale identity this is the tailnet login itself (Phase 9, Q2).
/// </param>
/// <param name="Email">
/// The account email. Under Tailscale identity this is the login verbatim, which is an email for
/// Google and Microsoft accounts and something email-shaped otherwise.
/// </param>
/// <param name="Name">The provider's profile name, used only as the initial display name.</param>
public sealed record ExternalLogin(string Subject, string Email, string? Name);
