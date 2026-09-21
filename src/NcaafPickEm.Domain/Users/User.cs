namespace NcaafPickEm.Domain.Users;

/// <summary>
/// A person who can sign in. Created the first time an identity is seen and matched on every
/// later request by <see cref="ExternalSubject"/>, never by email or display name (Feature 08).
/// </summary>
public sealed class User
{
    /// <summary>Maximum length of <see cref="DisplayName"/>, in characters.</summary>
    public const int DisplayNameMaxLength = 30;

    /// <summary>Maximum length of <see cref="ExternalSubject"/>, in characters.</summary>
    public const int ExternalSubjectMaxLength = 64;

    public Guid Id { get; set; }

    /// <summary>
    /// The identity provider's stable handle for this person, and the match key on every
    /// returning request. Under Tailscale identity headers it is the tailnet login verbatim
    /// (Phase 9, Q2); fixture demo users use <c>fixture:{name}</c>.
    /// </summary>
    public string ExternalSubject { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>Account-wide name. A league may override it with <c>Memberships.DisplayNameOverride</c>.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public DateTime LastLoginUtc { get; set; }
}
