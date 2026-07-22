using Microsoft.Extensions.Hosting;

namespace ECommerce.Api.Services;

public class AiBackgroundService : BackgroundService
{
    private readonly AiTaskQueue _taskQueue;
    private readonly ILogger<AiBackgroundService> _logger;

    public AiBackgroundService(AiTaskQueue taskQueue, ILogger<AiBackgroundService> logger)
    {
        _taskQueue = taskQueue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AI Background Service is running.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await _taskQueue.DequeueAsync(stoppingToken);
                await workItem(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Prevent throwing if stoppingToken was signaled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing AI background task.");
            }
        }
    }
}
