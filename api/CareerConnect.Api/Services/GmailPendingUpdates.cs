using System.Text.Json;
using System.Text.Json.Serialization;
using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

/// <summary>
/// The user's unreviewed email updates, kept until they're dealt with.
/// <para>
/// A scan advances its watermark, so each email is only ever found once. That
/// means whatever a scan finds has to survive until the user accepts or
/// dismisses it — clearing it on read, overwriting it with the next scan, or
/// dropping it when a window closes all lose it for good. Scheduled and manual
/// scans both write here, and the client reads from here.
/// </para>
/// </summary>
public interface IGmailPendingUpdates
{
    /// <summary>
    /// Everything waiting for review, minus updates that no longer apply.
    /// Auto-applied confirmations need no action, so reading them counts as
    /// seeing them. Null when there's nothing to show.
    /// </summary>
    Task<GmailScanResponse?> ReadAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Merges a scan's findings into what's already waiting; a newer email about the same thing replaces the older one.</summary>
    Task AddAsync(Guid userId, GmailScanResponse found, CancellationToken cancellationToken = default);

    Task RemoveStatusUpdateAsync(
        Guid userId, Guid applicationId, ApplicationStatus suggestedStatus, CancellationToken cancellationToken = default);

    Task RemoveNewApplicationAsync(Guid userId, string companyName, CancellationToken cancellationToken = default);
}

public class GmailPendingUpdates(AppDbContext db, ILogger<GmailPendingUpdates> logger) : IGmailPendingUpdates
{
    // Web defaults read both the camelCase written here and the PascalCase that
    // older rows used; string enums keep stored rows readable, and the converter
    // still accepts the numbers older rows were written with.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // Same exclusion the scanner uses: once an application is closed out, a
    // suggestion about it is moot.
    private static readonly ApplicationStatus[] Closed = [ApplicationStatus.Rejected, ApplicationStatus.Withdrawn];

    public async Task<GmailScanResponse?> ReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var connection = await FindConnectionAsync(userId, cancellationToken);
        if (connection?.PendingScanResultJson is null)
        {
            return null;
        }

        var stored = Deserialize(connection.PendingScanResultJson);

        var applications = await db.Applications
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => new { a.Id, a.CompanyName, a.RoleTitle, a.Status })
            .ToListAsync(cancellationToken);
        var byId = applications.ToDictionary(a => a.Id);

        // Drop anything the user already settled some other way — changed the
        // status themselves, deleted the application, added the company by
        // hand — and show the rest against the application as it is now.
        var statusUpdates = stored.StatusUpdates
            .Where(s => byId.TryGetValue(s.ApplicationId, out var app)
                     && app.Status != s.SuggestedStatus
                     && !Closed.Contains(app.Status))
            .Select(s =>
            {
                var app = byId[s.ApplicationId];
                return new SuggestedStatusUpdateResponse
                {
                    ApplicationId = s.ApplicationId,
                    CompanyName = app.CompanyName,
                    RoleTitle = app.RoleTitle,
                    CurrentStatus = app.Status,
                    SuggestedStatus = s.SuggestedStatus,
                    Reasoning = s.Reasoning,
                    EmailSubject = s.EmailSubject,
                    EmailFrom = s.EmailFrom,
                    EmailReceivedAtUtc = s.EmailReceivedAtUtc,
                    InterviewAtUtc = s.InterviewAtUtc,
                    InterviewKind = s.InterviewKind,
                };
            })
            .ToList();

        var newApplications = DistinctJobs(stored.NewApplications
            .Where(n => !applications.Any(a => CompanyNames.Same(a.CompanyName, n.CompanyName))));

        var autoApplied = stored.AutoApplied
            .Where(a => byId.ContainsKey(a.ApplicationId))
            .ToList();

        // Keep only what still needs a decision. A save with nothing changed is
        // a no-op, so reads don't write unless something was actually pruned.
        Store(connection, new Stored { StatusUpdates = statusUpdates, NewApplications = newApplications });
        await db.SaveChangesAsync(cancellationToken);

        return statusUpdates.Count == 0 && newApplications.Count == 0 && autoApplied.Count == 0
            ? null
            : new GmailScanResponse
            {
                StatusUpdates = statusUpdates,
                NewApplications = newApplications,
                AutoApplied = autoApplied,
            };
    }

    public async Task AddAsync(Guid userId, GmailScanResponse found, CancellationToken cancellationToken = default)
    {
        if (found.StatusUpdates.Count == 0 && found.NewApplications.Count == 0 && found.AutoApplied.Count == 0)
        {
            return;
        }

        var connection = await FindConnectionAsync(userId, cancellationToken);
        if (connection is null)
        {
            return; // Disconnected between the scan starting and finishing.
        }

        var stored = Deserialize(connection.PendingScanResultJson);

        // Keyed on what an update is *about*, so a follow-up email on the same
        // subject replaces the earlier one instead of listing it twice.
        Store(connection, new Stored
        {
            StatusUpdates = NewestPerKey(
                stored.StatusUpdates.Concat(found.StatusUpdates),
                s => (s.ApplicationId, s.SuggestedStatus),
                s => s.EmailReceivedAtUtc),
            NewApplications = DistinctJobs(stored.NewApplications.Concat(found.NewApplications)),
            AutoApplied = NewestPerKey(
                stored.AutoApplied.Concat(found.AutoApplied),
                a => a.ApplicationId,
                a => a.EmailReceivedAtUtc),
        });
        connection.PendingScanCompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveStatusUpdateAsync(
        Guid userId, Guid applicationId, ApplicationStatus suggestedStatus, CancellationToken cancellationToken = default)
    {
        var connection = await FindConnectionAsync(userId, cancellationToken);
        if (connection?.PendingScanResultJson is null)
        {
            return;
        }

        var stored = Deserialize(connection.PendingScanResultJson);
        Store(connection, stored with
        {
            StatusUpdates = stored.StatusUpdates
                .Where(s => !(s.ApplicationId == applicationId && s.SuggestedStatus == suggestedStatus))
                .ToList(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveNewApplicationAsync(
        Guid userId, string companyName, CancellationToken cancellationToken = default)
    {
        var connection = await FindConnectionAsync(userId, cancellationToken);
        if (connection?.PendingScanResultJson is null)
        {
            return;
        }

        var stored = Deserialize(connection.PendingScanResultJson);
        Store(connection, stored with
        {
            // Every spelling of the company goes with it, or dismissing "Delta
            // Dental" would leave "Delta Dental MO" behind.
            NewApplications = stored.NewApplications
                .Where(n => !CompanyNames.Same(n.CompanyName, companyName))
                .ToList(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<GmailConnection?> FindConnectionAsync(Guid userId, CancellationToken cancellationToken) =>
        db.GmailConnections.FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);

    /// <summary>Stores null once nothing's left — that's what HasPendingSuggestions reads.</summary>
    private static void Store(GmailConnection connection, Stored updates)
    {
        if (updates.IsEmpty)
        {
            connection.PendingScanResultJson = null;
            connection.PendingScanCompletedAtUtc = null;
            return;
        }

        connection.PendingScanResultJson = JsonSerializer.Serialize(updates, SerializerOptions);
    }

    private Stored Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Stored();
        }

        try
        {
            return JsonSerializer.Deserialize<Stored>(json, SerializerOptions) ?? new Stored();
        }
        catch (JsonException ex)
        {
            // An unreadable row costs the updates in it, not the whole feature.
            logger.LogWarning(ex, "Discarding unreadable pending Gmail updates.");
            return new Stored();
        }
    }

    /// <summary>
    /// One suggestion per job, newest email first. Several confirmations for
    /// one application — or one company written three ways — are the same job.
    /// </summary>
    private static List<SuggestedNewApplicationResponse> DistinctJobs(IEnumerable<SuggestedNewApplicationResponse> items)
    {
        var kept = new List<SuggestedNewApplicationResponse>();
        foreach (var item in items.OrderByDescending(n => n.EmailReceivedAtUtc))
        {
            if (!kept.Any(k => CompanyNames.Same(k.CompanyName, item.CompanyName) && CompanyNames.SameRole(k.RoleTitle, item.RoleTitle)))
            {
                kept.Add(item);
            }
        }
        return kept;
    }

    private static List<T> NewestPerKey<T, TKey>(
        IEnumerable<T> items, Func<T, TKey> key, Func<T, DateTime> receivedAtUtc) where TKey : notnull =>
        items
            .GroupBy(key)
            .Select(group => group.MaxBy(receivedAtUtc)!)
            .OrderByDescending(receivedAtUtc)
            .ToList();

    /// <summary>
    /// Storage shape, separate from <see cref="GmailScanResponse"/>: that
    /// contract's required members would reject rows written before one of these
    /// lists existed, and a missing list here simply reads as empty.
    /// </summary>
    private sealed record Stored
    {
        public List<SuggestedStatusUpdateResponse> StatusUpdates { get; init; } = [];
        public List<SuggestedNewApplicationResponse> NewApplications { get; init; } = [];
        public List<AutoAppliedResponse> AutoApplied { get; init; } = [];

        [JsonIgnore]
        public bool IsEmpty => StatusUpdates.Count == 0 && NewApplications.Count == 0 && AutoApplied.Count == 0;
    }
}
