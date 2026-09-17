using System.ComponentModel.DataAnnotations;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Contracts;

public class LoginRequest
{
    [Required, EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}

public class RegisterRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public required string Email { get; init; }

    [Required, MinLength(8), MaxLength(200)]
    public required string Password { get; init; }

    [MaxLength(200)]
    public string? DisplayName { get; init; }
}

public class ForgotPasswordRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public required string Email { get; init; }
}

public class ResetPasswordRequest
{
    [Required, MaxLength(200)]
    public required string Token { get; init; }

    [Required, MinLength(8), MaxLength(200)]
    public required string Password { get; init; }
}

public class LoginResponse
{
    public required string Token { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }

    /// <summary>
    /// Free or Pro. Sent with the token so the client can render the right app
    /// on first paint; it isn't a JWT claim, because a plan can change while a
    /// token is still valid and the client re-reads it from /api/auth/me.
    /// </summary>
    public required PlanTier Plan { get; init; }
}

/// <summary>Who the caller is, re-fetched on load so a plan change lands without signing out.</summary>
public class MeResponse
{
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public required PlanTier Plan { get; init; }
}
