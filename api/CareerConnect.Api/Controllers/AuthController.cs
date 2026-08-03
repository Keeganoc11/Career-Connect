using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, ITokenService tokenService) : ControllerBase
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user is null ||
            PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password)
                == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid credentials",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        var (token, expiresAtUtc) = tokenService.CreateToken(user);
        return Ok(new LoginResponse
        {
            Token = token,
            Email = user.Email,
            DisplayName = user.DisplayName,
            ExpiresAtUtc = expiresAtUtc
        });
    }
}
