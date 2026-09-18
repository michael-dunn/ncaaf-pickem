namespace NcaafPickEm.Api.Auth;

/// <summary>
/// The signed-in user for the current request. Handlers take this instead of reading claims.
/// </summary>
/// <remarks>
/// Claim shapes are an implementation detail of <see cref="ExternalSignInService"/> and the test
/// auth handler; keeping them behind this interface means the test scheme and the cookie scheme
/// are interchangeable everywhere.
/// </remarks>
public interface ICurrentUser
{
    /// <summary>True when the request carries a valid principal.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Our <c>Users.Id</c>.</summary>
    /// <exception cref="InvalidOperationException">The request is anonymous.</exception>
    Guid UserId { get; }

    /// <summary>The account-wide display name, or empty when anonymous.</summary>
    string DisplayName { get; }

    /// <summary>The account email, or empty when anonymous.</summary>
    string Email { get; }

    /// <summary>The user id, or null when the request is anonymous.</summary>
    Guid? TryGetUserId();
}
