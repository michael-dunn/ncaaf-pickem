namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// Every route table row in <c>Implementation/03-API-Contracts.md</c>, transcribed by hand
/// (P8-01). <see cref="RouteInventoryTests"/> asserts each one is actually mapped, so a route the
/// contract promises cannot quietly go missing and a contract change has to be made here too.
/// </summary>
/// <remarks>
/// Route constraints are stripped, so entries read exactly as the contract tables do.
/// <c>/auth/callback/google</c> is absent on purpose: it has no endpoint of its own, it is the
/// Google handler's <c>CallbackPath</c> (03-API-Contracts.md, Auth).
/// </remarks>
internal static class ApiContractRoutes
{
    /// <summary>Method + route for every contracted endpoint.</summary>
    public static readonly string[] All =
    [
        // Auth (Feature 08)
        "GET /auth/login/google",
        "POST /auth/logout",
        "GET /api/me",
        "PUT /api/me",

        // Leagues and members (Feature 01)
        "POST /api/leagues",
        "GET /api/leagues",
        "GET /api/leagues/{leagueId}",
        "PUT /api/leagues/{leagueId}/settings",
        "GET /api/leagues/{leagueId}/members",
        "PUT /api/leagues/{leagueId}/members/me/display-name",
        "POST /api/leagues/{leagueId}/invites",
        "GET /api/leagues/{leagueId}/invites",
        "DELETE /api/leagues/{leagueId}/invites/{inviteId}",
        "GET /api/invites/{code}",
        "POST /api/invites/{code}/accept",
        "DELETE /api/leagues/{leagueId}/members/{membershipId}",
        "POST /api/leagues/{leagueId}/members/{membershipId}/promote",
        "POST /api/leagues/{leagueId}/members/{membershipId}/demote",
        "POST /api/leagues/{leagueId}/commissioner/transfer",

        // Season calendar (Feature 13)
        "GET /api/seasons/{year}/weeks",
        "GET /api/leagues/{leagueId}/weeks",

        // Game set configuration (Feature 02)
        "GET /api/leagues/{leagueId}/gameset-rules",
        "PUT /api/leagues/{leagueId}/gameset-rules",
        "GET /api/leagues/{leagueId}/weeks/{week}/gameset-rules",
        "PUT /api/leagues/{leagueId}/weeks/{week}/gameset-rules",
        "POST /api/leagues/{leagueId}/weeks/{week}/gameset/preview",
        "POST /api/leagues/{leagueId}/weeks/{week}/gameset/generate",
        "POST /api/leagues/{leagueId}/weeks/{week}/gameset/games",
        "DELETE /api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}",
        "GET /api/leagues/{leagueId}/weeks/{week}/gameset",
        "GET /api/seasons/{year}/weeks/{week}/games",
        "GET /api/reference/conferences",
        "GET /api/reference/teams",

        // Point values (Feature 03)
        "GET /api/leagues/{leagueId}/point-rules",
        "PUT /api/leagues/{leagueId}/point-rules",
        "PUT /api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/points",

        // Picks (Feature 04)
        "GET /api/leagues/{leagueId}/weeks/{week}/picks/me",
        "PUT /api/leagues/{leagueId}/weeks/{week}/picks/me/{gameId}",
        "POST /api/leagues/{leagueId}/weeks/{week}/picks/me/submit",
        "POST /api/leagues/{leagueId}/weeks/{week}/picks/me/ack-changes",
        "GET /api/leagues/{leagueId}/weeks/{week}/picks",
        "GET /api/leagues/{leagueId}/weeks/{week}/picks/status",

        // Influence dashboard (Feature 05)
        "GET /api/leagues/{leagueId}/weeks/{week}/dashboard",

        // Scoring and corrections (Feature 06)
        "POST /api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/override-result",
        "POST /api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/void",
        "GET /api/leagues/{leagueId}/audit",

        // Leaderboard (Feature 07)
        "GET /api/leagues/{leagueId}/leaderboard",
        "GET /api/leagues/{leagueId}/weeks/{week}/leaderboard",
        "GET /api/leagues/{leagueId}/weeks/{week}/grid",

        // Data admin (Features 09, 12)
        "GET /api/admin/data-status",
        "POST /api/admin/refresh/{dataType}",
        "POST /api/admin/unmatched/{id}/resolve",

        // Push (Feature 11)
        "GET /api/push/vapid-public-key",
        "POST /api/push/subscriptions",
        "DELETE /api/push/subscriptions",
        "GET /api/push/status",
        "POST /api/push/test",
        "GET /api/leagues/{leagueId}/notifications/log",

        // Health
        "GET /health",
        "GET /health/ready",
    ];
}
