using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.GameSets;

/// <summary>One game-selection rule (Feature 02). Rules are unioned; <paramref name="SortOrder"/> is display only.</summary>
/// <param name="RuleId">Existing rule id, or null for a rule being created (PUT full-replace and preview).</param>
/// <param name="RuleType">Top25, Conference, or Team.</param>
/// <param name="ConferenceId">Conference rules only.</param>
/// <param name="ConferenceName">Read-only echo for display.</param>
/// <param name="TeamId">Team rules only.</param>
/// <param name="TeamName">Read-only echo for display.</param>
/// <param name="ConferenceGamesOnly">Conference rules only: both teams must be in the conference.</param>
/// <param name="SortOrder">Display position.</param>
public sealed record GameSetRuleDto(
    Guid? RuleId,
    GameSetRuleType RuleType,
    Guid? ConferenceId,
    string? ConferenceName,
    Guid? TeamId,
    string? TeamName,
    bool ConferenceGamesOnly,
    int SortOrder);
