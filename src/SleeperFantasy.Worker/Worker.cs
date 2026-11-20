using SleeperFantasy.Application.Features.Analysis;

namespace SleeperFantasy.Worker;

public class Worker(
    ILogger<Worker> logger,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuredRunId = configuration["analysis-run-id"]
            ?? Environment.GetEnvironmentVariable("ANALYSIS_RUN_ID");
        var executionId = Environment.GetEnvironmentVariable("CLOUD_RUN_EXECUTION");
        if (Guid.TryParse(configuredRunId, out var runId))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<IAnalysisRunExecutor>()
                .ExecuteAsync(runId, executionId, stoppingToken);
            lifetime.StopApplication();
            return;
        }

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<IAnalysisRunExecutor>()
                .ExecuteAsync(null, executionId, stoppingToken);
        }
        logger.LogInformation("Worker completed its one-shot execution. ExecutionId={ExecutionId}", executionId);
        lifetime.StopApplication();
    }
}
