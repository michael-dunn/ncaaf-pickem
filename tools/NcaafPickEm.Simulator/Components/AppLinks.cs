namespace NcaafPickEm.Simulator.Components;

/// <summary>Links into the app, signing in through its Development-only <c>/auth/dev-login</c> first.</summary>
public static class AppLinks
{
    /// <summary>
    /// Opens the app as <paramref name="login"/> at <paramref name="returnPath"/>. The dev-login
    /// cookie is per browser, so this switches the whole browser to that player.
    /// </summary>
    public static string SignIn(string appBaseUrl, string login, string returnPath) =>
        $"{appBaseUrl}/auth/dev-login?user={Uri.EscapeDataString(login)}&returnUrl={Uri.EscapeDataString(returnPath)}";

    /// <summary>The league's picks page for a week.</summary>
    public static string Picks(Guid leagueId, int week) => $"/leagues/{leagueId}/weeks/{week}/picks";

    /// <summary>The league's dashboard for a week.</summary>
    public static string Dashboard(Guid leagueId, int week) => $"/leagues/{leagueId}/weeks/{week}/dashboard";

    /// <summary>The league's week leaderboard.</summary>
    public static string WeekLeaderboard(Guid leagueId, int week) => $"/leagues/{leagueId}/weeks/{week}/leaderboard";

    /// <summary>The league's season leaderboard.</summary>
    public static string Leaderboard(Guid leagueId) => $"/leagues/{leagueId}/leaderboard";

    /// <summary>The league's week games page (the commissioner adds and removes games there).</summary>
    public static string Games(Guid leagueId, int week) => $"/leagues/{leagueId}/weeks/{week}/games";

    /// <summary>The league's home page.</summary>
    public static string League(Guid leagueId) => $"/leagues/{leagueId}";
}
