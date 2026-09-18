using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers.Models;

/// <summary>
/// A conference as any reference-data provider reports it, independent of CFBD or ESPN's own
/// shape (Feature 09, 12). See <c>Implementation/spikes/providers.md</c> for the field mapping
/// each real provider will use to populate this record.
/// </summary>
public sealed record ProviderConference(
    int CfbdId,
    string Name,
    string Abbreviation,
    TeamClassification Classification);
