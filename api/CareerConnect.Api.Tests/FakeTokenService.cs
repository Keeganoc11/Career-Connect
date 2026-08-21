using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class FakeTokenService : ITokenService
{
    public int CallCount { get; private set; }
    public Guid? LastUserId { get; private set; }

    public (string Token, DateTime ExpiresAtUtc) CreateToken(User user)
    {
        CallCount++;
        LastUserId = user.Id;
        return ($"fake-token-for-{user.Id}", DateTime.UtcNow.AddHours(12));
    }
}
