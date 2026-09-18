using NcaafPickEm.Domain.Users;

namespace NcaafPickEm.Domain.Notifications;

/// <summary>
/// One browser's web-push subscription (Feature 11). A user has one per device.
/// </summary>
public sealed class PushSubscription
{
    /// <summary>Maximum length of <see cref="Endpoint"/>, in characters.</summary>
    public const int EndpointMaxLength = 2048;

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The push service URL the browser handed us. Unique across all users.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>SHA-256 of <see cref="Endpoint"/>, computed by SQL Server.</summary>
    /// <remarks>
    /// <see cref="Endpoint"/> is too long to be an index key (SQL Server allows 1700 bytes),
    /// so uniqueness and lookup both run through this persisted computed column.
    /// Never assign it: the database owns the value.
    /// </remarks>
    public byte[] EndpointHash { get; set; } = [];

    /// <summary>Client public key from the subscription's <c>keys.p256dh</c>.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>Client auth secret from the subscription's <c>keys.auth</c>.</summary>
    public string Auth { get; set; } = string.Empty;

    public string? UserAgent { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? LastSuccessUtc { get; set; }

    /// <summary>Consecutive delivery failures. Reset on success; the subscription is pruned when it climbs.</summary>
    public int FailureCount { get; set; }
}
