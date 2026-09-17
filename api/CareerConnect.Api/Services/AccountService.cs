using CareerConnect.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public enum DeleteAccountOutcome
{
    Deleted,
    NotFound,
    /// <summary>The typed confirmation didn't match the account's email.</summary>
    ConfirmationMismatch,
}

public interface IAccountService
{
    /// <summary>Everything the account holds, as one object ready to serialize.</summary>
    Task<object?> ExportAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<DeleteAccountOutcome> DeleteAsync(
        Guid userId, string confirmationEmail, CancellationToken cancellationToken = default);
}

public class AccountService(
    AppDbContext db,
    IGmailOAuthService gmail,
    ILogger<AccountService> logger) : IAccountService
{
    public async Task<object?> ExportAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.DisplayName, u.Plan, u.CreatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return null;
        }

        // Read whole rather than shaped for a screen: the point of an export is
        // that nothing is left behind, so anything the account holds is here.
        var applications = await db.Applications
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Include(a => a.StatusHistory)
            .Include(a => a.Interviews)
            .ThenInclude(i => i.Questions)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var resumes = await db.Resumes
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.UpdatedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.Label,
                r.Content,
                r.ExtraFacts,
                r.IsActive,
                r.UpdatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var activity = await db.ActivityEvents
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new
        {
            ExportedAtUtc = DateTime.UtcNow,
            Account = user,
            Applications = applications.Select(a => new
            {
                a.Id,
                a.CompanyName,
                a.RoleTitle,
                a.Status,
                a.DateApplied,
                a.JobPostingUrl,
                a.JobDescriptionText,
                a.Notes,
                a.CoverLetterText,
                a.TailoredResumeText,
                a.CreatedAtUtc,
                a.UpdatedAtUtc,
                StatusHistory = a.StatusHistory.Select(h => new
                {
                    h.FromStatus,
                    h.ToStatus,
                    h.ChangedAtUtc,
                    h.Source,
                }),
                Interviews = a.Interviews.Select(i => new
                {
                    i.Id,
                    i.ScheduledAtUtc,
                    i.Kind,
                    i.Notes,
                    i.ResearchNotes,
                    i.Reflection,
                    i.SelfRating,
                    i.Debrief,
                    Questions = i.Questions.Select(q => new
                    {
                        q.Side,
                        q.Kind,
                        q.Text,
                        q.Answer,
                        q.Quality,
                        q.Asked,
                        q.Suggested,
                    }),
                }),
            }),
            Resumes = resumes,
            Activity = activity.Select(a => new
            {
                a.Trigger,
                a.FromStatus,
                a.ToStatus,
                a.Reasoning,
                a.CreatedAtUtc,
            }),
        };
    }

    public async Task<DeleteAccountOutcome> DeleteAsync(
        Guid userId, string confirmationEmail, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return DeleteAccountOutcome.NotFound;
        }

        // Typing the address is the whole safety mechanism for something with
        // no undo, so it has to match — case and spacing aside.
        if (!string.Equals(confirmationEmail.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
        {
            return DeleteAccountOutcome.ConfirmationMismatch;
        }

        // Before the row goes: hand the Google grant back. Afterwards there's
        // no token left to revoke with, and the user would be left with an
        // app listed in their Google account that no longer exists.
        await gmail.DisconnectAsync(userId, cancellationToken);

        // Everything else hangs off the user by a cascading foreign key.
        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Account {UserId} was deleted at the owner's request.", userId);
        return DeleteAccountOutcome.Deleted;
    }
}
