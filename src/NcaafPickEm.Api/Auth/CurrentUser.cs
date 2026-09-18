using System.Security.Claims;

namespace NcaafPickEm.Api.Auth;

/// <summary>
/// Reads <see cref="ICurrentUser"/> off the ambient <see cref="HttpContext"/>'s principal.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates the accessor.</summary>
    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    /// <inheritdoc />
    public bool IsAuthenticated => TryGetUserId() is not null;

    /// <inheritdoc />
    public Guid UserId => TryGetUserId()
        ?? throw new InvalidOperationException("The current request is not authenticated.");

    /// <inheritdoc />
    public string DisplayName => Principal?.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    /// <inheritdoc />
    public string Email => Principal?.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

    /// <inheritdoc />
    public Guid? TryGetUserId()
    {
        ClaimsPrincipal? principal = Principal;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId)
            ? userId
            : null;
    }
}
