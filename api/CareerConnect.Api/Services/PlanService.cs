using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public interface IPlanService
{
    /// <summary>The plan this user is on right now.</summary>
    Task<PlanTier> GetPlanAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> IsProAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Filters a set of user ids down to the ones on Pro. Background work runs
    /// over every user, and asking once beats a query per user.
    /// </summary>
    Task<HashSet<Guid>> FilterProAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// Answers "can this account use the automated features?".
///
/// Two ways to be on Pro: the plan stored on the row, or an email listed in
/// <c>Billing:ProEmails</c>. The config list is what keeps the owner's own
/// account (and anyone he comps) working without a subscription, and it stays
/// authoritative rather than being copied onto the row at signup, so adding an
/// email to it takes effect for accounts that already exist.
/// </summary>
public class PlanService(AppDbContext db, IConfiguration configuration) : IPlanService
{
    // Emails are compared case-insensitively: nobody types their own address
    // the same way twice, and the config list is maintained by hand.
    private readonly HashSet<string> _complimentary = ReadComplimentaryEmails(configuration);

    public async Task<PlanTier> GetPlanAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.Plan })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return PlanTier.Free;
        }

        return user.Plan == PlanTier.Pro || _complimentary.Contains(user.Email)
            ? PlanTier.Pro
            : PlanTier.Free;
    }

    public async Task<bool> IsProAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await GetPlanAsync(userId, cancellationToken) == PlanTier.Pro;

    public async Task<HashSet<Guid>> FilterProAsync(
        IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var users = await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.Plan })
            .ToListAsync(cancellationToken);

        return users
            .Where(u => u.Plan == PlanTier.Pro || _complimentary.Contains(u.Email))
            .Select(u => u.Id)
            .ToHashSet();
    }

    /// <summary>
    /// Accepts either a JSON array (Billing:ProEmails:0) or one comma-separated
    /// string — Railway variables can only be the latter.
    /// </summary>
    private static HashSet<string> ReadComplimentaryEmails(IConfiguration configuration)
    {
        var section = configuration.GetSection("Billing:ProEmails");
        var values = section.Get<string[]>() ?? [];

        if (values.Length == 0 && section.Value is { Length: > 0 } single)
        {
            values = single.Split(',');
        }

        return values
            .Select(email => email.Trim())
            .Where(email => email.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
