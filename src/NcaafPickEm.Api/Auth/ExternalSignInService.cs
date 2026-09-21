using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Api.Auth;

/// <summary>
/// Turns an external identity into one of our users, and (for the cookie path) one of our
/// cookies.
/// </summary>
/// <remarks>
/// The external principal never becomes the app's principal: we upsert <c>Users</c> by
/// <see cref="User.ExternalSubject"/> and then build a principal of our own, so every downstream
/// claim is one we control and a rename at the identity provider cannot reshape a request.
/// <see cref="UpsertAsync"/> is the half <see cref="TailscaleAuthenticationHandler"/> runs on
/// every request; <see cref="SignInAsync"/> adds the cookie and, since P9-03, has exactly one
/// caller left: <c>/auth/dev-login</c> in Development and Testing.
/// </remarks>
public sealed class ExternalSignInService
{
    /// <summary>
    /// How stale <see cref="User.LastLoginUtc"/> has to be before a matched login rewrites it.
    /// </summary>
    /// <remarks>
    /// Header identity authenticates every request, so writing the timestamp each time would turn
    /// every GET - every static file, every poll - into an UPDATE. An hour's resolution is more
    /// than the column is ever read at.
    /// </remarks>
    public static readonly TimeSpan LastLoginWriteInterval = TimeSpan.FromHours(1);

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
    /// <remarks>
    /// The cookie path exists for <c>/auth/dev-login</c> only. A tailnet caller is authenticated
    /// per request by <see cref="TailscaleAuthenticationHandler"/> and never signs in at all.
    /// </remarks>
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
            CreatePrincipal(user, CookieAuthenticationDefaults.AuthenticationScheme),
            new AuthenticationProperties { IsPersistent = true });

        return user;
    }

    /// <summary>
    /// Finds the user <paramref name="login"/> names, creating the row the first time that
    /// identity is seen.
    /// </summary>
    /// <param name="login">The external identity, already validated as non-empty.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The matched or created user.</returns>
    /// <remarks>
    /// Matching is on <see cref="ExternalLogin.Subject"/> alone. On a match the email follows the
    /// provider, because it is the account's identity everywhere else, but the display name never
    /// does: it is the user's to change (Feature 08 Profile).
    /// </remarks>
    public async Task<User> UpsertAsync(ExternalLogin login, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(login);

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        User? user = await _database.Users
            .FirstOrDefaultAsync(candidate => candidate.ExternalSubject == login.Subject, cancellationToken);

        if (user is not null)
        {
            if (!string.Equals(user.Email, login.Email, StringComparison.Ordinal))
            {
                user.Email = login.Email;
            }

            if (nowUtc - user.LastLoginUtc > LastLoginWriteInterval)
            {
                user.LastLoginUtc = nowUtc;
            }

            // No-op when neither changed, which is the common case under header identity.
            await _database.SaveChangesAsync(cancellationToken);
            return user;
        }

        user = new User
        {
            Id = Guid.CreateVersion7(),
            ExternalSubject = login.Subject,
            Email = login.Email,
            DisplayName = ToInitialDisplayName(login.Name, login.Email),
            CreatedUtc = nowUtc,
            LastLoginUtc = nowUtc,
        };

        _database.Users.Add(user);

        try
        {
            await _database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Header identity authenticates every request, so a browser's first page load can
            // race several requests into this insert at once. Whichever one lost the unique
            // index simply reads the winner's row back.
            _database.Entry(user).State = EntityState.Detached;

            User? winner = await _database.Users
                .FirstOrDefaultAsync(candidate => candidate.ExternalSubject == login.Subject, cancellationToken);

            if (winner is null)
            {
                throw;
            }

            _logger.LogDebug("Lost the race to create a user for a first sign-in; using {UserId}", winner.Id);
            return winner;
        }

        _logger.LogInformation("Created user {UserId} from a first sign-in", user.Id);
        return user;
    }

    /// <summary>Builds the claims principal a signed-in request carries.</summary>
    /// <param name="user">The signed-in user.</param>
    /// <param name="authenticationType">
    /// The scheme the identity is attributed to, so the principal reports the scheme that
    /// actually authenticated the request.
    /// </param>
    public static ClaimsPrincipal CreatePrincipal(User user, string authenticationType)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Email, user.Email),
            ],
            authenticationType,
            ClaimTypes.Name,
            ClaimTypes.Role);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>Trims a provider-supplied name to what <c>Users.DisplayName</c> can hold.</summary>
    public static string ToInitialDisplayName(string? externalName, string email)
    {
        string candidate = string.IsNullOrWhiteSpace(externalName)
            ? email.Split('@')[0]
            : externalName.Trim();

        if (candidate.Length > User.DisplayNameMaxLength)
        {
            candidate = candidate[..User.DisplayNameMaxLength].TrimEnd();
        }

        return candidate.Length == 0 ? "Player" : candidate;
    }
}
