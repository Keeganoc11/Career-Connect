using System.ComponentModel.DataAnnotations;

namespace CareerConnect.Api.Contracts;

public class LoginRequest
{
    [Required, EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}

public class LoginResponse
{
    public required string Token { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}
