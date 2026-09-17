using System.Security.Cryptography;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public enum ResetOutcome
{
    Done,
    /// <summary>Unknown, already used, or expired — the caller is told one thing for all three.</summary>
    InvalidOrExpired,
}

public interface IPasswordResetService
{
    /// <summary>
    /// Emails a reset link when the address belongs to an account. Returns
    /// nothing either way: whether an account exists is not something an
    /// anonymous caller gets to learn.
    /// </summary>
    Task RequestAsync(string email, CancellationToken cancellationToken = default);

    Task<ResetOutcome> ResetAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}

public class PasswordResetService(
    AppDbContext db,
    IEmailSender email,
    IConfiguration configuration,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

    /// <summary>
    /// Long enough to read the mail and find a password manager, short enough
    /// that a link sitting in an inbox isn't a standing key to the account.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public async Task RequestAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        var normalized = emailAddress.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);

        if (user is null)
        {
            // Deliberately silent. The endpoint answers the same either way,
            // so this can't be used to find out who has an account.
            logger.LogInformation("Password reset requested for an address with no account.");
            return;
        }

        // Asking again replaces any outstanding link rather than adding to it:
        // two live links are two chances for the wrong person to find one.
        var outstanding = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAtUtc == null)
            .ToListAsync(cancellationToken);
        db.PasswordResetTokens.RemoveRange(outstanding);

        var raw = GenerateToken();
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = Hash(raw),
            ExpiresAtUtc = DateTime.UtcNow.Add(Lifetime),
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        var link = $"{PublicUrl()}/reset?token={Uri.EscapeDataString(raw)}";
        var sent = await email.SendAsync(new EmailMessage(
            user.Email,
            "Reset your Career Connect password",
            $"""
             <p>Hi{(user.DisplayName is { Length: > 0 } name ? $" {System.Net.WebUtility.HtmlEncode(name)}" : "")},</p>
             <p>Use the link below to set a new password. It works once and expires in an hour.</p>
             <p><a href="{link}">Set a new password</a></p>
             <p>If you didn't ask for this, you can ignore this email — nothing has changed.</p>
             """,
            $"""
             Use this link to set a new Career Connect password. It works once and expires in an hour.

             {link}

             If you didn't ask for this, you can ignore this email — nothing has changed.
             """), cancellationToken);

        if (!sent)
        {
            logger.LogError("A reset token was created but the email could not be sent.");
        }
    }

    public async Task<ResetOutcome> ResetAsync(
        string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var hash = Hash(token);
        var stored = await db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null || stored.UsedAtUtc is not null || stored.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return ResetOutcome.InvalidOrExpired;
        }

        stored.User.PasswordHash = PasswordHasher.HashPassword(stored.User, newPassword);

        // Everything signed in before this moment stops working. Someone
        // resetting because they were compromised expects the other session to
        // end, not to keep running until its token expires on its own.
        stored.User.TokenVersion++;

        stored.UsedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return ResetOutcome.Done;
    }

    /// <summary>Where the links point. Falls back to the production domain rather than localhost.</summary>
    private string PublicUrl() =>
        (configuration["App:PublicUrl"] ?? "https://careerconnectapp.com").TrimEnd('/');

    private static string GenerateToken() =>
        // 32 bytes, URL-safe. Guid would be smaller and partly predictable;
        // this is what the whole flow's security rests on.
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
