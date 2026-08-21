using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Tests;

public sealed class AuthServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeTokenService _tokenService = new();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _service = new AuthService(_fixture.Db, _tokenService);
    }

    public void Dispose() => _fixture.Dispose();

    private async Task<User> SeedUserWithRealPasswordAsync(string email, string password)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        _fixture.Db.Users.Add(user);
        await _fixture.Db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task LoginAsync_SucceedsWithCorrectPassword()
    {
        await SeedUserWithRealPasswordAsync("me@example.com", "correct-horse-battery-staple");

        var outcome = await _service.LoginAsync("me@example.com", "correct-horse-battery-staple");

        var success = Assert.IsType<LoginOutcome.Success>(outcome);
        Assert.Equal("me@example.com", success.Response.Email);
        Assert.Equal(1, _tokenService.CallCount);
    }

    [Fact]
    public async Task LoginAsync_FailsWithWrongPassword()
    {
        await SeedUserWithRealPasswordAsync("me@example.com", "correct-horse-battery-staple");

        var outcome = await _service.LoginAsync("me@example.com", "wrong-password");

        Assert.IsType<LoginOutcome.InvalidCredentials>(outcome);
        Assert.Equal(0, _tokenService.CallCount);
    }

    [Fact]
    public async Task LoginAsync_FailsForUnknownEmail()
    {
        var outcome = await _service.LoginAsync("nobody@example.com", "whatever");

        Assert.IsType<LoginOutcome.InvalidCredentials>(outcome);
    }

    [Fact]
    public async Task RegisterAsync_CreatesUserAndReturnsToken()
    {
        var outcome = await _service.RegisterAsync("new@example.com", "a-strong-password", "New Person");

        var success = Assert.IsType<RegisterOutcome.Success>(outcome);
        Assert.Equal("new@example.com", success.Response.Email);
        Assert.Equal("New Person", success.Response.DisplayName);
        Assert.Equal(1, await _fixture.Db.Users.CountAsync());
    }

    [Fact]
    public async Task RegisterAsync_HashesThePassword_NotStoredInPlainText()
    {
        await _service.RegisterAsync("new@example.com", "a-strong-password", null);

        var stored = await _fixture.Db.Users.SingleAsync();
        Assert.NotEqual("a-strong-password", stored.PasswordHash);
        Assert.NotEmpty(stored.PasswordHash);
    }

    [Fact]
    public async Task RegisterAsync_AllowsLoggingInAfterwardsWithThatPassword()
    {
        await _service.RegisterAsync("new@example.com", "a-strong-password", null);

        var login = await _service.LoginAsync("new@example.com", "a-strong-password");

        Assert.IsType<LoginOutcome.Success>(login);
    }

    [Fact]
    public async Task RegisterAsync_FailsWhenEmailAlreadyRegistered()
    {
        await SeedUserWithRealPasswordAsync("taken@example.com", "whatever");

        var outcome = await _service.RegisterAsync("taken@example.com", "a-strong-password", null);

        Assert.IsType<RegisterOutcome.EmailAlreadyRegistered>(outcome);
        Assert.Equal(1, await _fixture.Db.Users.CountAsync());
    }

    [Fact]
    public async Task RegisterAsync_TrimsEmailAndDisplayName()
    {
        await _service.RegisterAsync("  spaced@example.com  ", "a-strong-password", "  Spaced Name  ");

        var stored = await _fixture.Db.Users.SingleAsync();
        Assert.Equal("spaced@example.com", stored.Email);
        Assert.Equal("Spaced Name", stored.DisplayName);
    }
}
