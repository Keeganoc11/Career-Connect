using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CareerConnect.Api.Tests;

public sealed class PasswordResetServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakeEmailSender _email = new();
    private readonly PasswordResetService _service;

    public PasswordResetServiceTests()
    {
        _service = new PasswordResetService(
            _fixture.Db,
            _email,
            new ConfigurationBuilder()
                .AddInMemoryCollection([new("App:PublicUrl", "https://careerconnectapp.com")])
                .Build(),
            NullLogger<PasswordResetService>.Instance);
    }

    public void Dispose() => _fixture.Dispose();

    private async Task<User> SeedUserAsync(string email = "me@example.com", string password = "original-password")
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

    private static bool PasswordWorks(User user, string password) =>
        new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, password)
            != PasswordVerificationResult.Failed;

    [Fact]
    public async Task RequestAsync_EmailsALinkAndStoresOnlyTheHash()
    {
        var user = await SeedUserAsync();

        await _service.RequestAsync(user.Email);

        var message = Assert.Single(_email.Sent);
        Assert.Equal(user.Email, message.To);
        Assert.Contains("https://careerconnectapp.com/reset?token=", message.TextBody);

        var token = _email.LastToken;
        Assert.False(string.IsNullOrWhiteSpace(token));

        // The raw token must not be recoverable from the database.
        var stored = await _fixture.Db.PasswordResetTokens.SingleAsync();
        Assert.DoesNotContain(token!, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
    }

    [Fact]
    public async Task RequestAsync_SaysNothingAboutAnAddressWithNoAccount()
    {
        // No throw, no mail, no row — and the caller can't tell this apart
        // from a successful request.
        await _service.RequestAsync("nobody@example.com");

        Assert.Empty(_email.Sent);
        Assert.Empty(await _fixture.Db.PasswordResetTokens.ToListAsync());
    }

    [Fact]
    public async Task RequestAsync_ReplacesAnOutstandingLinkRatherThanAddingOne()
    {
        var user = await SeedUserAsync();

        await _service.RequestAsync(user.Email);
        var first = _email.LastToken!;
        await _service.RequestAsync(user.Email);
        var second = _email.LastToken!;

        Assert.NotEqual(first, second);
        Assert.Single(await _fixture.Db.PasswordResetTokens.ToListAsync());
        Assert.Equal(ResetOutcome.InvalidOrExpired, await _service.ResetAsync(first, "brand-new-password"));
        Assert.Equal(ResetOutcome.Done, await _service.ResetAsync(second, "brand-new-password"));
    }

    [Fact]
    public async Task ResetAsync_SetsThePasswordAndEndsExistingSessions()
    {
        var user = await SeedUserAsync();
        var versionBefore = user.TokenVersion;
        await _service.RequestAsync(user.Email);

        var outcome = await _service.ResetAsync(_email.LastToken!, "brand-new-password");

        Assert.Equal(ResetOutcome.Done, outcome);
        var reloaded = await _fixture.Db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.True(PasswordWorks(reloaded, "brand-new-password"));
        Assert.False(PasswordWorks(reloaded, "original-password"));
        // Every token issued before now is refused (see Program.cs).
        Assert.Equal(versionBefore + 1, reloaded.TokenVersion);
    }

    [Fact]
    public async Task ResetAsync_WorksOnce()
    {
        var user = await SeedUserAsync();
        await _service.RequestAsync(user.Email);
        var token = _email.LastToken!;

        Assert.Equal(ResetOutcome.Done, await _service.ResetAsync(token, "brand-new-password"));
        Assert.Equal(ResetOutcome.InvalidOrExpired, await _service.ResetAsync(token, "another-password"));

        var reloaded = await _fixture.Db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.True(PasswordWorks(reloaded, "brand-new-password"));
    }

    [Fact]
    public async Task ResetAsync_RefusesAnExpiredLink()
    {
        var user = await SeedUserAsync();
        await _service.RequestAsync(user.Email);
        var token = _email.LastToken!;

        var stored = await _fixture.Db.PasswordResetTokens.SingleAsync();
        stored.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await _fixture.Db.SaveChangesAsync();

        Assert.Equal(ResetOutcome.InvalidOrExpired, await _service.ResetAsync(token, "brand-new-password"));
        var reloaded = await _fixture.Db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.True(PasswordWorks(reloaded, "original-password"));
    }

    [Fact]
    public async Task ResetAsync_RefusesAMadeUpToken() =>
        Assert.Equal(
            ResetOutcome.InvalidOrExpired,
            await _service.ResetAsync("not-a-real-token", "brand-new-password"));

    [Fact]
    public async Task RequestAsync_KeepsTheTokenEvenWhenSendingFails()
    {
        // The row stays, so a retry replaces it rather than leaving the user
        // with a token they were never shown — and the failure is logged.
        var user = await SeedUserAsync();
        _email.NextSendFails = true;

        await _service.RequestAsync(user.Email);

        Assert.Single(await _fixture.Db.PasswordResetTokens.ToListAsync());
    }
}
