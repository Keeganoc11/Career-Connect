using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public interface IInterviewService
{
    Task<List<InterviewEventResponse>> ListAsync(Guid userId, Guid applicationId);

    /// <summary>One interview as an .ics file, or null if it isn't the caller's.</summary>
    Task<string?> BuildCalendarFileAsync(Guid userId, Guid interviewId);
    Task<InterviewEventResponse?> CreateAsync(Guid userId, Guid applicationId, CreateInterviewRequest request);
    Task<InterviewEventResponse?> UpdateAsync(Guid userId, Guid interviewId, UpdateInterviewRequest request);
    Task<bool> DeleteAsync(Guid userId, Guid interviewId);

    /// <summary>
    /// Records an interview read off an email. Returns null when one is already
    /// scheduled near that time, so a reminder or a forwarded copy of the same
    /// invite doesn't create a duplicate.
    /// </summary>
    Task<InterviewEvent?> RecordFromEmailAsync(
        Guid userId, Guid applicationId, DateTime scheduledAtUtc, InterviewKind kind, string? notes);
}

public class InterviewService(AppDbContext db, IInterviewCalendarSync calendar) : IInterviewService
{
    /// <summary>
    /// Two interviews for the same application within this window are treated as
    /// the same one. Wide enough to absorb a timezone misread or an email that
    /// only gave the hour, narrow enough to keep a genuine second round separate.
    /// </summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(4);

    public async Task<List<InterviewEventResponse>> ListAsync(Guid userId, Guid applicationId)
    {
        var interviews = await db.InterviewEvents
            .AsNoTracking()
            .Where(i => i.ApplicationId == applicationId && i.Application.UserId == userId)
            .OrderBy(i => i.ScheduledAtUtc)
            .ToListAsync();

        return interviews.Select(ToResponse).ToList();
    }

    public async Task<string?> BuildCalendarFileAsync(Guid userId, Guid interviewId)
    {
        var interview = await db.InterviewEvents
            .AsNoTracking()
            .Include(i => i.Application)
            .FirstOrDefaultAsync(i => i.Id == interviewId && i.Application.UserId == userId);

        return interview is null
            ? null
            : InterviewCalendarFile.Build(interview, interview.Application, DateTime.UtcNow);
    }

    public async Task<InterviewEventResponse?> CreateAsync(
        Guid userId, Guid applicationId, CreateInterviewRequest request)
    {
        var application = await db.Applications
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == applicationId);

        if (application is null)
        {
            return null;
        }

        var interview = new InterviewEvent
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            ScheduledAtUtc = DateTime.SpecifyKind(request.ScheduledAtUtc, DateTimeKind.Utc),
            Kind = request.Kind,
            Notes = NormalizeOptional(request.Notes),
            Source = ChangeSource.Manual,
            CreatedAtUtc = DateTime.UtcNow,
        };

        db.InterviewEvents.Add(interview);
        await db.SaveChangesAsync();

        await SyncToCalendarAsync(userId, interview, application);
        return ToResponse(interview);
    }

    public async Task<InterviewEventResponse?> UpdateAsync(
        Guid userId, Guid interviewId, UpdateInterviewRequest request)
    {
        var interview = await db.InterviewEvents
            .Include(i => i.Application)
            .FirstOrDefaultAsync(i => i.Id == interviewId && i.Application.UserId == userId);

        if (interview is null)
        {
            return null;
        }

        interview.ScheduledAtUtc = DateTime.SpecifyKind(request.ScheduledAtUtc, DateTimeKind.Utc);
        interview.Kind = request.Kind;
        interview.Notes = NormalizeOptional(request.Notes);
        await db.SaveChangesAsync();

        await SyncToCalendarAsync(userId, interview, interview.Application);
        return ToResponse(interview);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid interviewId)
    {
        var interview = await db.InterviewEvents
            .FirstOrDefaultAsync(i => i.Id == interviewId && i.Application.UserId == userId);

        if (interview is null)
        {
            return false;
        }

        // Remove the calendar copy first: if that fails we still delete here,
        // but a stranded calendar entry is worth one attempt to avoid.
        if (interview.CalendarEventId is not null)
        {
            await calendar.DeleteAsync(userId, interview.CalendarEventId);
        }

        db.InterviewEvents.Remove(interview);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<InterviewEvent?> RecordFromEmailAsync(
        Guid userId, Guid applicationId, DateTime scheduledAtUtc, InterviewKind kind, string? notes)
    {
        // Ownership first: never query another user's schedule, even to answer
        // a question whose result gets discarded.
        var application = await db.Applications
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == applicationId);

        if (application is null)
        {
            return null;
        }

        var scheduled = DateTime.SpecifyKind(scheduledAtUtc, DateTimeKind.Utc);
        var earliest = scheduled - DuplicateWindow;
        var latest = scheduled + DuplicateWindow;

        var alreadyScheduled = await db.InterviewEvents
            .AnyAsync(i => i.ApplicationId == applicationId
                        && i.ScheduledAtUtc >= earliest
                        && i.ScheduledAtUtc <= latest);

        if (alreadyScheduled)
        {
            return null;
        }

        var interview = new InterviewEvent
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            ScheduledAtUtc = scheduled,
            Kind = kind,
            Notes = NormalizeOptional(notes),
            Source = ChangeSource.EmailSuggestion,
            CreatedAtUtc = DateTime.UtcNow,
        };

        db.InterviewEvents.Add(interview);
        await db.SaveChangesAsync();

        await SyncToCalendarAsync(userId, interview, application);
        return interview;
    }

    /// <summary>
    /// Mirrors the interview onto the user's calendar. Never throws: the
    /// interview is saved either way, and a calendar outage must not look like
    /// a failure to schedule.
    /// </summary>
    private async Task SyncToCalendarAsync(Guid userId, InterviewEvent interview, Application application)
    {
        var calendarEventId = await calendar.UpsertAsync(userId, interview, application);
        if (calendarEventId is null || calendarEventId == interview.CalendarEventId)
        {
            return;
        }

        interview.CalendarEventId = calendarEventId;
        await db.SaveChangesAsync();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InterviewEventResponse ToResponse(InterviewEvent i) => new()
    {
        Id = i.Id,
        ApplicationId = i.ApplicationId,
        ScheduledAtUtc = i.ScheduledAtUtc,
        Kind = i.Kind,
        Notes = i.Notes,
        Source = i.Source,
        OnCalendar = i.CalendarEventId is not null,
    };
}
