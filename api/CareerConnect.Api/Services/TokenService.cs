using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CareerConnect.Api.Domain;
using Microsoft.IdentityModel.Tokens;

namespace CareerConnect.Api.Services;

public interface ITokenService
{
    /// <summary>The claim carrying <see cref="Domain.User.TokenVersion"/>.</summary>
    const string TokenVersionClaim = "tv";

    (string Token, DateTime ExpiresAtUtc) CreateToken(User user);
}

public class TokenService(IConfiguration config) : ITokenService
{
    private const string TokenVersionClaim = ITokenService.TokenVersionClaim;

    public (string Token, DateTime ExpiresAtUtc) CreateToken(User user)
    {
        var key = config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var expiryMinutes = config.GetValue("Jwt:ExpiryMinutes", 60 * 12);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                // Checked against the user row on every authenticated request:
                // a password reset bumps it and every token issued before is
                // refused (see Program.cs, OnTokenValidated).
                new Claim(TokenVersionClaim, user.TokenVersion.ToString())
            ],
            expires: expiresAtUtc,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
