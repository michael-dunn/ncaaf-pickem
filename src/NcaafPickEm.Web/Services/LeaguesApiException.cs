namespace NcaafPickEm.Web.Services;

/// <summary>
/// Thrown by <see cref="ILeaguesApi"/> and <see cref="ISeasonsApi"/> implementations when the API
/// answers with a non-success status this client does not special-case (see 03-API-Contracts.md
/// "Errors use ProblemDetails"). The exception's message is the ProblemDetails title, safe to
/// show the user directly.
/// </summary>
public sealed class LeaguesApiException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="statusCode">HTTP status code of the response.</param>
    /// <param name="message">User-facing message, taken from the ProblemDetails title.</param>
    /// <param name="errors">Validation errors dictionary, when the response carried one.</param>
    /// <param name="count">The ProblemDetails <c>extensions["count"]</c> value, when present.</param>
    public LeaguesApiException(
        int statusCode,
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null,
        int? count = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
        Count = count;
    }

    /// <summary>HTTP status code of the response that caused this exception.</summary>
    public int StatusCode { get; }

    /// <summary>Field-level validation errors, when the 400 response carried an <c>errors</c> dictionary.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    /// <summary>
    /// The ProblemDetails <c>extensions["count"]</c> value, when present (e.g. Picks'
    /// <c>IncompletePicks</c> 409 or GameSets' <c>ExceedsMax</c> 409, D-100). Null otherwise.
    /// </summary>
    public int? Count { get; }
}

/// <summary>Minimal shape read back from a <c>ProblemDetails</c> JSON body.</summary>
/// <param name="Title">Short, user-facing summary of the problem.</param>
/// <param name="Detail">Longer explanation, not always present.</param>
/// <param name="Errors">Field name -&gt; messages, present on 400 validation failures.</param>
/// <param name="Count">The <c>extensions["count"]</c> value, when the problem carries one.</param>
internal sealed record ProblemDetailsBody(
    string? Title,
    string? Detail,
    Dictionary<string, string[]>? Errors,
    int? Count);
