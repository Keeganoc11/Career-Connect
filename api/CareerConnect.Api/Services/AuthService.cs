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

public interface IAuthService
{
    Task<LoginOutcome> LoginAsync(string email, string password);
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

        var (token, expiresAtUtc) = tokenService.CreateToken(user);
        return new LoginOutcome.Success(new LoginResponse
        {
            Token = token,
            Email = user.Email,
            DisplayName = user.DisplayName,
            ExpiresAtUtc = expiresAtUtc,
        });
    }
}
