using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public interface IAgendaService
{
    /// <summary>What needs attention right now: interviews coming up, and applications that have gone quiet.</summary>
    Task<AgendaResponse> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Answers "what do I do today" from dates and statuses alone. Deliberately
/// deterministic rather than a model call: this is arithmetic on timestamps,
/// it should be free and instant, and the same inputs must always produce the
/// same list. <see cref="ICopilotService"/> is the AI counterpart for judgement.
/// </summary>
public class AgendaService(AppDbContext db) : IAgendaService
{
    /// <summary>Far enough ahead to plan around, close enough to still be "soon".</summary>
    private static readonly TimeSpan UpcomingWindow = TimeSpan.FromDays(14);

    /// <summary>Prep cleared the bar this long ago and you still haven't applied.</summary>
    private const int ReadyToApplyIdleDays = 2;

    /// <summary>Found a posting this long ago and never ran prep on it.</summary>
    private const int NeverPreppedIdleDays = 7;

    /// <summary>Applied, and nothing has come back.</summary>
    private const int SilentAfterApplyingDays = 14;

    /// <summary>Silent this long is, realistically, a no.</summary>
    private const int ProbablyGhostedDays = 30;

    /// <summary>Mid-process and it's gone quiet — worth a nudge sooner than a fresh application.</summary>
    private const int SilentMidProcessDays = 7;

    /// <summary>An offer shouldn't sit unanswered.</summary>
    private const int OfferIdleDays = 3;

    private static readonly ApplicationStatus[] Terminal =
        [ApplicationStatus.Rejected, ApplicationStatus.Ghosted, ApplicationStatus.Withdrawn];

    public async Task<AgendaResponse> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;

        var applications = await db.Applications
            .AsNoTracking()
            .Include(a => a.Interviews)
            .Where(a => a.UserId == userId && !Terminal.Contains(a.Status))
            .ToListAsync(cancellationToken);

        // Latest prep run per application, for the "you prepped this and stopped" nudge.
        var applicationIds = applications.Select(a => a.Id).ToList();
        var latestPrep = await db.PrepRuns
            .AsNoTracking()
            .Where(r => applicationIds.Contains(r.ApplicationId))
            .GroupBy(r => r.ApplicationId)
            .Select(g => g.OrderByDescending(r => r.StartedAtUtc).First())
            .ToListAsync(cancellationToken);

        var prepByApplication = latestPrep.ToDictionary(r => r.ApplicationId);

        var upcoming = applications
            .SelectMany(a => a.Interviews.Select(i => (Application: a, Interview: i)))
            .Where(x => x.Interview.ScheduledAtUtc >= utcNow
                     && x.Interview.ScheduledAtUtc <= utcNow + UpcomingWindow)
            .OrderBy(x => x.Interview.ScheduledAtUtc)
            .Select(x => new UpcomingInterviewResponse
            {
                InterviewId = x.Interview.Id,
                ApplicationId = x.Application.Id,
                CompanyName = x.Application.CompanyName,
                RoleTitle = x.Application.RoleTitle,
                ScheduledAtUtc = x.Interview.ScheduledAtUtc,
                Kind = x.Interview.Kind,
                Notes = x.Interview.Notes,
                OnCalendar = x.Interview.CalendarEventId is not null,
                HasPrep = x.Application.InterviewPrepJson is not null,
            })
            .ToList();

        var nudges = applications
            .Select(a => BuildNudge(a, prepByApplication.GetValueOrDefault(a.Id), utcNow))
            .OfType<AgendaNudgeResponse>()
            // Longest-neglected first: the ones most at risk of being forgotten.
            .OrderByDescending(n => n.DaysSinceActivity)
            .ToList();

        return new AgendaResponse { UpcomingInterviews = upcoming, Nudges = nudges };
    }

    /// <summary>
    /// At most one nudge per application — a list that says three things about
    /// the same row is noise, and the most urgent one is the only actionable one.
    /// </summary>
    private static AgendaNudgeResponse? BuildNudge(Application application, PrepRun? prep, DateTime utcNow)
    {
        var idleDays = (int)(utcNow - application.UpdatedAtUtc).TotalDays;

        // Anything with an interview on the books isn't stalled, whatever the
        // clock says — and it's already shown under Coming up, where the date
        // gives it context a nudge can't. Missing prep is flagged on that card.
        if (application.Interviews.Any(i => i.ScheduledAtUtc >= utcNow))
        {
            return null;
        }

        return application.Status switch
        {
            ApplicationStatus.Preparing when prep?.ReadyToApply == true && idleDays >= ReadyToApplyIdleDays =>
                Nudge(application, NudgeKind.ReadyToApply, idleDays,
                    "Prep cleared the bar — this is ready to send."),

            ApplicationStatus.Preparing when prep is null && idleDays >= NeverPreppedIdleDays =>
                Nudge(application, NudgeKind.NeverPrepped, idleDays,
                    $"Saved {idleDays} days ago and never prepped."),

            ApplicationStatus.Applied when idleDays >= ProbablyGhostedDays =>
                Nudge(application, NudgeKind.ProbablyGhosted, idleDays,
                    $"Silent for {idleDays} days — probably worth marking ghosted."),

            ApplicationStatus.Applied when idleDays >= SilentAfterApplyingDays =>
                Nudge(application, NudgeKind.Silent, idleDays,
                    $"No response in {idleDays} days. Worth a follow-up."),

            ApplicationStatus.PhoneScreen or ApplicationStatus.Interview when idleDays >= SilentMidProcessDays =>
                Nudge(application, NudgeKind.Silent, idleDays,
                    $"No next step scheduled in {idleDays} days. Follow up."),

            ApplicationStatus.Offer when idleDays >= OfferIdleDays =>
                Nudge(application, NudgeKind.AwaitingYou, idleDays,
                    "This offer is waiting on your answer."),

            _ => null,
        };
    }

    private static AgendaNudgeResponse Nudge(
        Application application, NudgeKind kind, int idleDays, string message) => new()
    {
        ApplicationId = application.Id,
        CompanyName = application.CompanyName,
        RoleTitle = application.RoleTitle,
        Status = application.Status,
        Kind = kind,
        DaysSinceActivity = idleDays,
        Message = message,
    };
}
