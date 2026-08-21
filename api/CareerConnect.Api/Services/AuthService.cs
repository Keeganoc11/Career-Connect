using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public abstract record LoginOutcome
{
    public sealed record Success(LoginResponse Response) : LoginOutcome;
    public sealed record InvalidCredentials : LoginOutcome;
}

public abstract record RegisterOutcome
{
    public sealed record Success(LoginResponse Response) : RegisterOutcome;
    public sealed record EmailAlreadyRegistered : RegisterOutcome;
}

public interface IAuthService
{
    Task<LoginOutcome> LoginAsync(string email, string password);

    /// <summary>Creates a new user and logs them in immediately, same response shape as LoginAsync.</summary>
    Task<RegisterOutcome> RegisterAsync(string email, string password, string? displayName);
}

public class AuthService(AppDbContext db, ITokenService tokenService) : IAuthService
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

    public async Task<LoginOutcome> LoginAsync(string email, string password)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null ||
            PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return new LoginOutcome.InvalidCredentials();
        }

        return new LoginOutcome.Success(ToLoginResponse(user));
    }

    public async Task<RegisterOutcome> RegisterAsync(string email, string password, string? displayName)
    {
        var normalizedEmail = email.Trim();
        if (await db.Users.AnyAsync(u => u.Email == normalizedEmail))
        {
            return new RegisterOutcome.EmailAlreadyRegistered();
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            PasswordHash = string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
        };
        user.PasswordHash = PasswordHasher.HashPassword(user, password);

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two concurrent registrations for the same email both passed the
            // AnyAsync check above; the unique index on Email is what
            // actually enforces this, and this is the second one to land.
            return new RegisterOutcome.EmailAlreadyRegistered();
        }

        return new RegisterOutcome.Success(ToLoginResponse(user));
    }

    private LoginResponse ToLoginResponse(User user)
    {
        var (token, expiresAtUtc) = tokenService.CreateToken(user);
        return new LoginResponse
        {
            Token = token,
            Email = user.Email,
            DisplayName = user.DisplayName,
            ExpiresAtUtc = expiresAtUtc,
        };
    }
}
