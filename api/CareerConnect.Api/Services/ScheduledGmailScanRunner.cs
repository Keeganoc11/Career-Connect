using System.Text.Json;
using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public interface IScheduledGmailScanRunner
{
    /// <summary>
    /// Runs a scan for every connected Gmail user and stores any findings for
    /// later review (see GmailConnection.PendingScanResultJson). Never
    /// applies anything itself, same as a manual scan — per-connection
    /// failures are logged and skipped rather than aborting the whole run.
    /// </summary>
    Task RunAllAsync(CancellationToken cancellationToken = default);
}

public class ScheduledGmailScanRunner(
    AppDbContext db,
    IGmailUpdateScanner scanner,
    ILogger<ScheduledGmailScanRunner> logger) : IScheduledGmailScanRunner
{
    public async Task RunAllAsync(CancellationToken cancellationToken = default)
    {
        var userIds = await db.GmailConnections
            .Select(g => g.UserId)
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            try
            {
                await RunOneAsync(userId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled Gmail scan failed for user {UserId}.", userId);
            }
        }
    }

    private async Task RunOneAsync(Guid userId, CancellationToken cancellationToken)
    {
        var outcome = await scanner.ScanAsync(userId, cancellationToken);
        if (outcome is not GmailScanOutcome.Success success)
        {
            // Not configured, disconnected mid-cycle, or a transient failure
            // (e.g. Gmail rate limit) — try again next cycle rather than
            // surfacing a background error nobody's watching for.
            return;
        }

        if (success.StatusUpdates.Count == 0 && success.NewApplications.Count == 0)
        {
            return;
        }

        var connection = await db.GmailConnections.FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);
        if (connection is null)
        {
            return; // Disconnected between the scan starting and finishing.
        }

        var response = new GmailScanResponse
        {
            StatusUpdates = success.StatusUpdates,
            NewApplications = success.NewApplications,
        };

        // Overwrites any previous pending result rather than merging — the
        // scan that just ran already covers everything since the watermark,
        // so it supersedes whatever an earlier cycle found.
        connection.PendingScanResultJson = JsonSerializer.Serialize(response);
        connection.PendingScanCompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
