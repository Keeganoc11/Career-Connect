using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public interface IScheduledGmailScanRunner
{
    /// <summary>
    /// Runs a scan for every connected Gmail user and adds any findings to
    /// their pending updates (see <see cref="IGmailPendingUpdates"/>). Never
    /// applies anything itself, same as a manual scan — per-connection
    /// failures are logged and skipped rather than aborting the whole run.
    /// </summary>
    Task RunAllAsync(CancellationToken cancellationToken = default);
}

public class ScheduledGmailScanRunner(
    AppDbContext db,
    IGmailUpdateScanner scanner,
    IGmailPendingUpdates pendingUpdates,
    IPlanService plans,
    ILogger<ScheduledGmailScanRunner> logger) : IScheduledGmailScanRunner
{
    public async Task RunAllAsync(CancellationToken cancellationToken = default)
    {
        var userIds = await db.GmailConnections
            .Select(g => g.UserId)
            .ToListAsync(cancellationToken);

        // A connection can outlive the plan that created it — someone who
        // downgrades keeps the stored token until they disconnect, and their
        // mail must stop being read the moment they stop paying for it.
        var pro = await plans.FilterProAsync(userIds, cancellationToken);

        foreach (var userId in userIds.Where(pro.Contains))
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

        // Merged, never overwritten: the scan just moved its watermark past
        // these emails, so anything an earlier cycle found that the user hasn't
        // handled yet would otherwise be gone for good.
        await pendingUpdates.AddAsync(userId, new GmailScanResponse
        {
            StatusUpdates = success.StatusUpdates,
            NewApplications = success.NewApplications,
            AutoApplied = success.AutoApplied,
        }, cancellationToken);
    }
}
