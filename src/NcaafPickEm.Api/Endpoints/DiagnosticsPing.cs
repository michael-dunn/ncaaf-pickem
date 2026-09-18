namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Body of the development-only league scoping probes. Deleted with
/// <see cref="DiagnosticsEndpoints"/> in P1-01.
/// </summary>
/// <param name="MembershipId">The membership the endpoint filter resolved.</param>
/// <param name="LeagueId">The league from the route.</param>
/// <param name="Role">The caller's role in that league.</param>
public sealed record DiagnosticsPing(Guid MembershipId, Guid LeagueId, string Role);
