using System.Net.Http.Json;
using System.Text.Json;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Shared response handling for real API clients (<see cref="LeaguesApi"/>,
/// <see cref="GameSetsApi"/>, <see cref="PointRulesApi"/>): ProblemDetails -&gt;
/// <see cref="LeaguesApiException"/> with a user-safe message, and JSON deserialization.
/// </summary>
/// <remarks>
/// Extracted so P3-05's new clients do not need to touch <see cref="LeaguesApi"/> (a different
/// task's file) to reuse this logic.
/// </remarks>
internal static class ApiResponses
{
    /// <summary>Deserializes the body, or null when there is none.</summary>
    public static async Task<T?> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>();

    /// <summary>Deserializes the body, throwing if the server sent an empty response.</summary>
    public static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response)
    {
        T? value = await ReadAsync<T>(response);
        return value ?? throw new LeaguesApiException((int)response.StatusCode, "The server sent an empty response.");
    }

    /// <summary>Throws <see cref="LeaguesApiException"/> for a non-success response.</summary>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ProblemDetailsBody? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>();
        }
        catch (JsonException)
        {
            // Not every failure response is a ProblemDetails body (e.g. a 401 with no content).
        }

        string message = problem?.Title
            ?? problem?.Detail
            ?? $"Request failed with status {(int)response.StatusCode}.";

        throw new LeaguesApiException((int)response.StatusCode, message, problem?.Errors, problem?.Count);
    }
}
