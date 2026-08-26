using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

/// <summary>
/// Drains the prep queue one run at a time. Serial on purpose: a run is a chain
/// of model calls, and letting several overlap would multiply spend and rate
/// limits for no perceptible gain — the user is watching one at a time.
/// </summary>
public class PrepRunBackgroundService(
    IPrepRunQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PrepRunBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReleaseInterruptedRunsAsync(stoppingToken);

        await foreach (var prepRunId in queue.DequeueAllAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IApplicationPrepRunner>();
            try
            {
                await runner.ExecuteAsync(prepRunId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Prep run {PrepRunId} threw outside its own error handling.", prepRunId);
            }
        }
    }

    /// <summary>
    /// A deploy or crash mid-run leaves a Running row nothing will ever finish,
    /// and the UI would poll it forever. Nothing survives the process to resume,
    /// so close them out with a message that says to try again.
    /// </summary>
    private async Task ReleaseInterruptedRunsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var interrupted = await db.PrepRuns
            .Where(r => r.Status == PrepRunStatus.Running)
            .ToListAsync(cancellationToken);

        if (interrupted.Count == 0)
        {
            return;
        }

        foreach (var run in interrupted)
        {
            run.Status = PrepRunStatus.Failed;
            run.ReadyToApply = false;
            run.ErrorMessage = "This prep run was interrupted by a server restart. Run it again.";
            run.CompletedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Closed {Count} prep run(s) interrupted by a restart.", interrupted.Count);
    }
}
