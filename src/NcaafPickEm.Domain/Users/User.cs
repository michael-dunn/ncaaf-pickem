namespace NcaafPickEm.Domain.Users;

/// <summary>
/// A person who can sign in. Created on the first Google callback and matched on every later
/// login by <see cref="GoogleSubject"/>, never by email or display name (Feature 08).
/// </summary>
public sealed class User
{
    /// <summary>Maximum length of <see cref="DisplayName"/>, in characters.</summary>
    public const int DisplayNameMaxLength = 30;

    public Guid Id { get; set; }

    /// <summary>Google's stable <c>sub</c> claim. The match key on a returning login.</summary>
    public string GoogleSubject { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>Account-wide name. A league may override it with <c>Memberships.DisplayNameOverride</c>.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public DateTime LastLoginUtc { get; set; }
}
