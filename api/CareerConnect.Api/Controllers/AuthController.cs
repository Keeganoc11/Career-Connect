using CareerConnect.Api.Contracts;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CareerConnect.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(IAuthService authService) : ControllerBase
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
}
