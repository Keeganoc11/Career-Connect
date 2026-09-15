using System.Text;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

/// <summary>
/// Renders one interview as an iCalendar file. The zero-setup path onto a
/// calendar: no OAuth scope, no consent, and it opens in Apple Calendar and
/// Outlook as readily as Google's.
/// </summary>
public static class InterviewCalendarFile
{
    private const string LineBreak = "\r\n";

    public static string Build(InterviewEvent interview, Application application, DateTime stampUtc)
    {
        var start = DateTime.SpecifyKind(interview.ScheduledAtUtc, DateTimeKind.Utc);

        var description = new List<string> { $"{application.RoleTitle} at {application.CompanyName}" };
        if (!string.IsNullOrWhiteSpace(interview.Notes))
        {
            description.Add(interview.Notes);
        }

        if (!string.IsNullOrWhiteSpace(application.JobPostingUrl))
        {
            description.Add(application.JobPostingUrl);
        }

        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Career Connect//Interview//EN",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            // Stable per interview, so re-importing updates the event rather
            // than stacking a duplicate next to it.
            $"UID:{interview.Id}@careerconnect",
            $"DTSTAMP:{Stamp(stampUtc)}",
            $"DTSTART:{Stamp(start)}",
            $"DTEND:{Stamp(start.AddHours(1))}",
            $"SUMMARY:{Escape($"{DescribeKind(interview.Kind)} — {application.CompanyName}")}",
            $"DESCRIPTION:{Escape(string.Join("\n", description))}",
            "END:VEVENT",
            "END:VCALENDAR",
        };

        return string.Join(LineBreak, lines) + LineBreak;
    }

    private static string Stamp(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'");

    /// <summary>RFC 5545 §3.3.11: backslash, semicolon, comma and newline all carry meaning in a value.</summary>
    private static string Escape(string value) => new StringBuilder(value)
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n")
        .ToString();

    private static string DescribeKind(InterviewKind kind) => kind switch
    {
        InterviewKind.PhoneScreen => "Phone screen",
        InterviewKind.Technical => "Technical interview",
        InterviewKind.Onsite => "Onsite",
        InterviewKind.Final => "Final round",
        _ => "Interview",
    };
}
