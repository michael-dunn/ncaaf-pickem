using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Operations;

/// <summary>
/// A provider game we could not tie to a <c>Games</c> row (Feature 12). Shown on the data status
/// page so a commissioner can resolve it, which creates <c>TeamAliases</c> for next time.
/// </summary>
public sealed class UnmatchedGame
{
    /// <summary>Maximum length of <see cref="RawHomeName"/> and <see cref="RawAwayName"/>.</summary>
    public const int RawNameMaxLength = 200;

    public Guid Id { get; set; }

    public ProviderSource Source { get; set; }

    public string RawHomeName { get; set; } = string.Empty;

    public string RawAwayName { get; set; } = string.Empty;

    public DateOnly GameDate { get; set; }

    /// <summary>The provider payload verbatim, so a human can see what arrived.</summary>
    public string RawPayload { get; set; } = string.Empty;

    public DateTime FirstSeenUtc { get; set; }

    public DateTime? ResolvedUtc { get; set; }
}
