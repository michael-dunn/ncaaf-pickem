using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Api.Auth;

/// <summary>
/// Turns a Google ticket into one of our users and one of our cookies (Feature 08, Option A).
/// </summary>
/// <remarks>
/// Google's principal never becomes the app's principal: we upsert <c>Users</c> by
/// <c>GoogleSubject</c> and then sign in a principal of our own, so every downstream claim is
/// one we control and a Google profile rename cannot reshape a session.
/// </remarks>
public sealed class ExternalSignInService
{
    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExternalSignInService> _logger;

    /// <summary>Creates the service.</summary>
    public ExternalSignInService(
        AppDbContext database,
        TimeProvider timeProvider,
        ILogger<ExternalSignInService> logger)
    {
        _database = database;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Upserts the user for <paramref name="login"/> and issues the session cookie on
    /// <paramref name="httpContext"/>.
    /// </summary>
    public async Task<User> SignInAsync(
        HttpContext httpContext,
        ExternalLogin login,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(login);

        User user = await UpsertAsync(login, cancellationToken);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            CreatePrincipal(user),
            new AuthenticationProperties { IsPersistent = true });

        return user;
    }

    /// <summary>Builds the claims principal a signed-in request carries.</summary>
    public static ClaimsPrincipal CreatePrincipal(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Email, user.Email),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>Trims a Google profile name to what <c>Users.DisplayName</c> can hold.</summary>
    public static string ToInitialDisplayName(string? googleName, string email)
    {
        string candidate = string.IsNullOrWhiteSpace(googleName)
            ? email.Split('@')[0]
            : googleName.Trim();

        if (candidate.Length > User.DisplayNameMaxLength)
        {
            candidate = candidate[..User.DisplayNameMaxLength].TrimEnd();
        }

        return candidate.Length == 0 ? "Player" : candidate;
    }

    private async Task<User> UpsertAsync(ExternalLogin login, CancellationToken cancellationToken)
    {
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        User? user = await _database.Users
            .FirstOrDefaultAsync(candidate => candidate.GoogleSubject == login.Subject, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Id = Guid.CreateVersion7(),
                GoogleSubject = login.Subject,
                Email = login.Email,
                DisplayName = ToInitialDisplayName(login.Name, login.Email),
                CreatedUtc = nowUtc,
                LastLoginUtc = nowUtc,
            };

            _database.Users.Add(user);
            _logger.LogInformation("Created user {UserId} from a first Google sign-in", user.Id);
        }
        else
        {
            // The display name is the user's to change (Feature 08 Profile); only the email
            // follows Google, because it is the account's identity everywhere else.
            user.Email = login.Email;
            user.LastLoginUtc = nowUtc;
            _logger.LogInformation("Matched existing user {UserId} by Google subject", user.Id);
        }

        await _database.SaveChangesAsync(cancellationToken);
        return user;
    }
}
