using CareerConnect.Api.Domain;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;

namespace CareerConnect.Api.Services;

public class GoogleInterviewCalendarSync(
    IGmailOAuthService oauth, ILogger<GoogleInterviewCalendarSync> logger) : IInterviewCalendarSync
{
    /// <summary>Emails rarely state an end time, and an hour is the common case for a screen or a round.</summary>
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(1);

    private const string PrimaryCalendar = "primary";

    public async Task<string?> UpsertAsync(
        Guid userId, InterviewEvent interview, Application application, CancellationToken cancellationToken = default)
    {
        using var calendar = await oauth.GetCalendarServiceAsync(userId, cancellationToken);
        if (calendar is null)
        {
            return null;
        }

        var payload = BuildEvent(interview, application);

        try
        {
            // An existing id means this interview already has a calendar copy —
            // update in place so a rescheduled time moves the event the user is
            // already looking at, rather than leaving two.
            if (interview.CalendarEventId is not null)
            {
                var updated = await calendar.Events
                    .Update(payload, PrimaryCalendar, interview.CalendarEventId)
                    .ExecuteAsync(cancellationToken);
                return updated.Id;
            }

            var created = await calendar.Events
                .Insert(payload, PrimaryCalendar)
                .ExecuteAsync(cancellationToken);
            return created.Id;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Couldn't sync interview {InterviewId} to Google Calendar for user {UserId}.",
                interview.Id, userId);
            return null;
        }
    }

    public async Task DeleteAsync(
        Guid userId, string calendarEventId, CancellationToken cancellationToken = default)
    {
        using var calendar = await oauth.GetCalendarServiceAsync(userId, cancellationToken);
        if (calendar is null)
        {
            return;
        }

        try
        {
            await calendar.Events.Delete(PrimaryCalendar, calendarEventId).ExecuteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Already gone (the user deleted it themselves) looks the same as a
            // real failure here, and neither should block removing our row.
            logger.LogWarning(
                ex, "Couldn't remove calendar event {CalendarEventId} for user {UserId}.", calendarEventId, userId);
        }
    }

    private static Event BuildEvent(InterviewEvent interview, Application application)
    {
        var start = new DateTimeOffset(DateTime.SpecifyKind(interview.ScheduledAtUtc, DateTimeKind.Utc));

        var description = new List<string> { $"{application.RoleTitle} at {application.CompanyName}" };
        if (!string.IsNullOrWhiteSpace(interview.Notes))
        {
            description.Add(interview.Notes);
        }

        if (!string.IsNullOrWhiteSpace(application.JobPostingUrl))
        {
            description.Add(application.JobPostingUrl);
        }

        description.Add("Scheduled by Career Connect.");

        return new Event
        {
            Summary = $"{DescribeKind(interview.Kind)} — {application.CompanyName}",
            Description = string.Join("\n\n", description),
            Start = new EventDateTime { DateTimeDateTimeOffset = start, TimeZone = "UTC" },
            End = new EventDateTime { DateTimeDateTimeOffset = start.Add(DefaultDuration), TimeZone = "UTC" },
        };
    }

    private static string DescribeKind(InterviewKind kind) => kind switch
    {
        InterviewKind.PhoneScreen => "Phone screen",
        InterviewKind.Technical => "Technical interview",
        InterviewKind.Onsite => "Onsite",
        InterviewKind.Final => "Final round",
        _ => "Interview",
    };
}
