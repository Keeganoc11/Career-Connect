using CareerConnect.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Data;

/// <summary>
/// Seeds the single local user from configuration (Seed:Email / Seed:Password).
/// Multi-user later means replacing this with a registration flow — nothing
/// else in the app assumes a single user.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration config, ILogger logger)
    {
        await db.Database.MigrateAsync();

        var email = config["Seed:Email"];
        var password = config["Seed:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Seed:Email / Seed:Password not configured; no user seeded.");
            return;
        }

        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = string.Empty,
            DisplayName = config["Seed:DisplayName"],
            CreatedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);

        db.Users.Add(user);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded user {Email}.", email);
    }
}
