using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(IAuthService authService, AppDbContext db, IPlanService plans) : ApiControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var outcome = await authService.LoginAsync(request.Email, request.Password);

        return outcome switch
        {
            LoginOutcome.Success success => Ok(success.Response),
            _ => Unauthorized(new ProblemDetails
            {
                Title = "Invalid credentials",
                Status = StatusCodes.Status401Unauthorized,
            }),
        };
    }

    /// <summary>Creates a new account and logs in immediately.</summary>
    [HttpPost("register")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoginResponse>> Register(RegisterRequest request)
    {
        var outcome = await authService.RegisterAsync(request.Email, request.Password, request.DisplayName);

        return outcome switch
        {
            RegisterOutcome.Success success => StatusCode(StatusCodes.Status201Created, success.Response),
            _ => Conflict(new ProblemDetails
            {
                Title = "An account with that email already exists.",
                Status = StatusCodes.Status409Conflict,
            }),
        };
    }

    /// <summary>
    /// The signed-in account, re-read on every app load. The token carries an
    /// id and an email but never a plan — an upgrade has to show up without
    /// making anyone sign out and back in.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    // The rate limit on this controller exists to slow down credential
    // guessing; this endpoint needs a token already and the client calls it on
    // every load, so counting it would lock out a user who just refreshes a lot.
    [DisableRateLimiting]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == UserId)
            .Select(u => new { u.Email, u.DisplayName })
            .FirstOrDefaultAsync(cancellationToken);

        // A valid token for a deleted account: nothing to describe.
        if (user is null)
        {
            return NotFound();
        }

        return Ok(new MeResponse
        {
            Email = user.Email,
            DisplayName = user.DisplayName,
            Plan = await plans.GetPlanAsync(UserId, cancellationToken),
        });
    }
}
