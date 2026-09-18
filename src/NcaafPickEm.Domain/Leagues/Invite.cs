namespace NcaafPickEm.Domain.Leagues;

/// <summary>
/// A shareable join code for a league (Feature 01). Codes are short and URL-safe so they can be
/// sent by text message.
/// </summary>
public sealed class Invite
{
    /// <summary>Maximum length of <see cref="Code"/>, in characters.</summary>
    public const int CodeMaxLength = 12;

    /// <summary>Default lifetime of a new invite.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(14);

    /// <summary>Default <see cref="MaxUses"/> for a new invite.</summary>
    public const int DefaultMaxUses = 50;

    public Guid Id { get; set; }

    public Guid LeagueId { get; set; }

    public League? League { get; set; }

    public string Code { get; set; } = string.Empty;

    public Guid CreatedByMembershipId { get; set; }

    public Membership? CreatedByMembership { get; set; }

    public DateTime ExpiresUtc { get; set; }

    public DateTime? RevokedUtc { get; set; }

    public int MaxUses { get; set; } = DefaultMaxUses;

    public int Uses { get; set; }
}
