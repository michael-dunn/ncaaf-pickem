using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// A provider's spelling of a team name, mapped to our <see cref="Team"/>. Seeded by hand and
/// grown by the matcher whenever a commissioner resolves an unmatched game (Feature 12).
/// </summary>
public sealed class TeamAlias
{
    /// <summary>Maximum length of <see cref="Alias"/>, in characters.</summary>
    public const int AliasMaxLength = 100;

    public Guid Id { get; set; }

    public Guid TeamId { get; set; }

    public Team? Team { get; set; }

    public ProviderSource Source { get; set; }

    public string Alias { get; set; } = string.Empty;
}
