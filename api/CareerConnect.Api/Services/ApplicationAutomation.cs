using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

/// <summary>What an email said, kept on the change it caused.</summary>
public record EmailEvidence(string Reasoning, string Subject, string From, DateTime ReceivedAtUtc);

public abstract record UndoOutcome
{
    public sealed record Undone(ActivityResponse Activity) : UndoOutcome;
    public sealed record NotFound : UndoOutcome;
    /// <summary>Already undone, or the application has moved on since — putting it back would clobber something newer.</summary>
    public sealed record Conflict(string Message) : UndoOutcome;
}

public interface IApplicationAutomation
{
    /// <summary>
    /// Applies a status change an email made clear, scheduling the interview it
    /// named if it named one, and records it for undo. Null when the application
    /// no longer exists or is already at that status.
    /// </summary>
    Task<ActivityEvent?> ApplyFromEmailAsync(
        Guid userId,
        Guid applicationId,
        ApplicationStatus toStatus,
        EmailEvidence evidence,
        (DateTime ScheduledAtUtc, InterviewKind Kind)? interview,
        CancellationToken cancellationToken = default);

    /// <summary>Marks every application silent past the threshold as ghosted. Returns how many it marked.</summary>
    Task<int> GhostSilentApplicationsAsync(CancellationToken cancellationToken = default);

    Task<List<ActivityResponse>> ListAsync(Guid userId, int days, CancellationToken cancellationToken = default);

    Task<UndoOutcome> UndoAsync(Guid userId, Guid activityId, CancellationToken cancellationToken = default);
}

/// <summary>Which email-suggested changes are clear-cut enough to make without asking.</summary>
public static class AutoApplyPolicy
{
    /// <summary>
    /// Forward moves and rejections only, and only when the email said so
    /// outright. An offer always waits for the user — it's the one update they
    /// should see with their own eyes — and nothing ever moves backwards on its
    /// own. A job still marked Preparing that skips straight past Applied is
    /// unusual enough to deserve a look, so only its confirmation is automatic.
    /// </summary>
    public static bool ShouldApply(ApplicationStatus current, ApplicationStatus suggested, bool clearCut)
    {
        // The user already decided to apply; a confirmation is just that decision coming back.
        if (current == ApplicationStatus.Preparing && suggested == ApplicationStatus.Applied)
        {
            return true;
        }

        if (!clearCut)
        {
            return false;
        }

        return (current, suggested) switch
        {
            (ApplicationStatus.Applied or ApplicationStatus.Ghosted or ApplicationStatus.PhoneScreen or ApplicationStatus.Interview,
                ApplicationStatus.Rejected) => true,
            (ApplicationStatus.Applied or ApplicationStatus.Ghosted, ApplicationStatus.PhoneScreen) => true,
            (ApplicationStatus.Applied or ApplicationStatus.Ghosted or ApplicationStatus.PhoneScreen, ApplicationStatus.Interview) => true,
            _ => false,
        };
    }
}

public class ApplicationAutomation(
    AppDbContext db,
    IInterviewService interviews,
    IConfiguration configuration,
    ILogger<ApplicationAutomation> logger) : IApplicationAutomation
{
    public async Task<ActivityEvent?> ApplyFromEmailAsync(
        Guid userId,
        Guid applicationId,
        ApplicationStatus toStatus,
        EmailEvidence evidence,
        (DateTime ScheduledAtUtc, InterviewKind Kind)? interview,
        CancellationToken cancellationToken = default)
    {
        var application = await db.Applications
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == applicationId, cancellationToken);

        if (application is null || application.Status == toStatus)
        {
            return null;
        }

        var activity = new ActivityEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ApplicationId = applicationId,
            Trigger = ActivityTrigger.Email,
            FromStatus = application.Status,
            ToStatus = toStatus,
            Reasoning = Truncate(evidence.Reasoning, 2000),
            EmailSubject = Truncate(evidence.Subject, 1000),
            EmailFrom = Truncate(evidence.From, 500),
            EmailReceivedAtUtc = evidence.ReceivedAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
        };

        // Dated to the confirmation, not today: that's when the application
        // actually landed, and the pipeline's date ordering depends on it.
        if (application.Status == ApplicationStatus.Preparing && toStatus == ApplicationStatus.Applied)
        {
            activity.PreviousDateApplied = application.DateApplied;
            application.DateApplied = DateOnly.FromDateTime(evidence.ReceivedAtUtc);
        }

        ChangeStatus(application, toStatus, ChangeSource.EmailAutomatic);
        db.ActivityEvents.Add(activity);
        await db.SaveChangesAsync(cancellationToken);

        if (interview is { } slot)
        {
            try
            {
                var scheduled = await interviews.RecordFromEmailAsync(
                    userId, applicationId, slot.ScheduledAtUtc, slot.Kind, notes: $"From: {evidence.Subject}");

                if (scheduled is not null)
                {
                    activity.InterviewEventId = scheduled.Id;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // The status change stands on its own; a calendar hiccup
                // shouldn't undo what the email plainly said.
                logger.LogWarning(ex, "Couldn't schedule the interview from activity {ActivityId}.", activity.Id);
            }
        }

        return activity;
    }

    public async Task<int> GhostSilentApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var days = configuration.GetValue("Automation:GhostAfterDays", 30);
        if (days <= 0)
        {
            return 0;
        }

        var utcNow = DateTime.UtcNow;
        var cutoff = utcNow.AddDays(-days);

        // UpdatedAtUtc moves on any change — including a logged follow-up — so
        // "silent" means nothing has happened on either side.
        var silent = await db.Applications
            .Where(a => a.Status == ApplicationStatus.Applied
                     && a.UpdatedAtUtc <= cutoff
                     && !a.Interviews.Any(i => i.ScheduledAtUtc >= utcNow))
            .ToListAsync(cancellationToken);

        foreach (var application in silent)
        {
            db.ActivityEvents.Add(new ActivityEvent
            {
                Id = Guid.NewGuid(),
                UserId = application.UserId,
                ApplicationId = application.Id,
                Trigger = ActivityTrigger.Inactivity,
                FromStatus = application.Status,
                ToStatus = ApplicationStatus.Ghosted,
                Reasoning = $"No reply and no activity for {(int)(utcNow - application.UpdatedAtUtc).TotalDays} days.",
                CreatedAtUtc = utcNow,
            });
            ChangeStatus(application, ApplicationStatus.Ghosted, ChangeSource.Inactivity);
        }

        await db.SaveChangesAsync(cancellationToken);
        return silent.Count;
    }

    public async Task<List<ActivityResponse>> ListAsync(Guid userId, int days, CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 90));

        var rows = await db.ActivityEvents
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.CreatedAtUtc >= since)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new
            {
                Activity = a,
                a.Application.CompanyName,
                a.Application.RoleTitle,
                CurrentStatus = a.Application.Status,
            })
            .Take(50)
            .ToListAsync(cancellationToken);

        var interviewIds = rows.Select(r => r.Activity.InterviewEventId).OfType<Guid>().ToList();
        var scheduled = await db.InterviewEvents
            .AsNoTracking()
            .Where(i => interviewIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        return rows
            .Select(r => ToResponse(r.Activity, r.CompanyName, r.RoleTitle, r.CurrentStatus,
                r.Activity.InterviewEventId is { } id ? scheduled.GetValueOrDefault(id) : null))
            .ToList();
    }

    public async Task<UndoOutcome> UndoAsync(Guid userId, Guid activityId, CancellationToken cancellationToken = default)
    {
        var activity = await db.ActivityEvents
            .Include(a => a.Application)
            .FirstOrDefaultAsync(a => a.Id == activityId && a.UserId == userId, cancellationToken);

        if (activity is null)
        {
            return new UndoOutcome.NotFound();
        }

        var application = activity.Application;

        if (activity.UndoneAtUtc is not null)
        {
            return new UndoOutcome.Conflict("That change was already undone.");
        }

        if (application.Status != activity.ToStatus)
        {
            return new UndoOutcome.Conflict(
                $"{application.CompanyName} has moved on since then, so there's nothing to undo. Change its status directly instead.");
        }

        if (activity.InterviewEventId is { } interviewId)
        {
            // Goes through the interview service so the calendar copy goes too.
            await interviews.DeleteAsync(userId, interviewId);
        }

        ChangeStatus(application, activity.FromStatus, ChangeSource.Manual);
        if (activity.PreviousDateApplied is { } previousDate)
        {
            application.DateApplied = previousDate;
        }
        activity.UndoneAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return new UndoOutcome.Undone(ToResponse(activity, application.CompanyName, application.RoleTitle, application.Status, null));
    }

    private void ChangeStatus(Application application, ApplicationStatus toStatus, ChangeSource source)
    {
        db.StatusChanges.Add(new StatusChange
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            FromStatus = application.Status,
            ToStatus = toStatus,
            ChangedAtUtc = DateTime.UtcNow,
            Source = source,
        });
        application.Status = toStatus;
    }

    private static ActivityResponse ToResponse(
        ActivityEvent a, string companyName, string roleTitle, ApplicationStatus currentStatus, InterviewEvent? interview) => new()
    {
        Id = a.Id,
        ApplicationId = a.ApplicationId,
        CompanyName = companyName,
        RoleTitle = roleTitle,
        Trigger = a.Trigger,
        FromStatus = a.FromStatus,
        ToStatus = a.ToStatus,
        InterviewAtUtc = interview?.ScheduledAtUtc,
        InterviewKind = interview?.Kind,
        Reasoning = a.Reasoning,
        EmailSubject = a.EmailSubject,
        EmailFrom = a.EmailFrom,
        EmailReceivedAtUtc = a.EmailReceivedAtUtc,
        CreatedAtUtc = a.CreatedAtUtc,
        UndoneAtUtc = a.UndoneAtUtc,
        CanUndo = a.UndoneAtUtc is null && currentStatus == a.ToStatus,
    };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
