namespace CareerConnect.Api.Services;

/// <summary>
/// Housekeeping that doesn't depend on Gmail: marking long-silent applications
/// ghosted. On its own timer, so it runs for users who never connected Gmail.
/// </summary>
public class AutomationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<AutomationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            using var scope = scopeFactory.CreateScope();
            var automation = scope.ServiceProvider.GetRequiredService<IApplicationAutomation>();
            try
            {
                var ghosted = await automation.GhostSilentApplicationsAsync(stoppingToken);
                if (ghosted > 0)
                {
                    logger.LogInformation("Marked {Count} silent application(s) as ghosted.", ghosted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ghosting sweep failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
