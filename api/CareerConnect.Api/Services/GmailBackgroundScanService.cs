namespace CareerConnect.Api.Services;

/// <summary>
/// Runs Gmail scans on a timer instead of only when the user clicks "Check
/// for updates" — the point of doing this at all is that a deployed,
/// always-on server can check while nobody's watching. Findings are stored
/// (GmailConnection.PendingScanResultJson) for the client to pick up next
/// time it loads; nothing here ever writes to an Application directly.
/// </summary>
public class GmailBackgroundScanService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<GmailBackgroundScanService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = configuration.GetValue("Gmail:ScanIntervalHours", 24);
        if (intervalHours <= 0)
        {
            logger.LogInformation("Scheduled Gmail scanning is disabled (Gmail:ScanIntervalHours <= 0).");
            return;
        }

        // A short delay before the first run so a `dotnet run` restart during
        // local dev iteration doesn't immediately burn a scan cycle.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));
        do
        {
            using var scope = scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IScheduledGmailScanRunner>();
            try
            {
                await runner.RunAllAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled Gmail scan cycle failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
