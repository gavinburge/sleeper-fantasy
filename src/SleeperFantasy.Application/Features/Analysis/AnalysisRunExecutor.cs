using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SleeperFantasy.Application.Abstractions;
using SleeperFantasy.Domain.Analysis;

namespace SleeperFantasy.Application.Features.Analysis;

public sealed class AnalysisRunExecutor(
    IAppDbContext dbContext,
    AnalysisRunService runService,
    IAnalysisOrchestrator orchestrator,
    AnalysisPersistenceService persistenceService,
    IAnalysisExecutionTrigger executionTrigger,
    ILogger<AnalysisRunExecutor> logger) : IAnalysisRunExecutor
{
    public async Task ExecuteAsync(
        Guid? analysisRunId,
        string? executionId,
        CancellationToken cancellationToken)
    {
        var run = analysisRunId is null
            ? await dbContext.AnalysisRuns
                .Where(x => x.Status == AnalysisRunStatus.Queued)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
            : await dbContext.AnalysisRuns
                .SingleOrDefaultAsync(x => x.Id == analysisRunId.Value, cancellationToken);
       
        if (run is null)
        {
            logger.LogInformation("No queued analysis run found. ExecutionId={ExecutionId}", executionId);
            return;
        }

        if (!await runService.ClaimAsync(run.Id, executionId, cancellationToken))
        {
            logger.LogInformation("Analysis run was already claimed or completed. AnalysisId={AnalysisId}", run.Id);
            return;
        }

        var configuration = await dbContext.ManagerConfigurations
            .SingleOrDefaultAsync(x => x.Id == run.ManagerConfigurationId, cancellationToken);
       
        if (configuration is null)
        {
            await persistenceService.PersistFailureAsync(
                run,
                "CONFIGURATION_NOT_FOUND",
                "The saved league configuration no longer exists.",
                false,
                cancellationToken);
            
            return;
        }

        try
        {
            await runService.HeartbeatAsync(
                run.Id,
                "refreshing",
                "source-refresh",
                executionId,
                cancellationToken);
            
            var review = await orchestrator.AnalyzeAsync(configuration, cancellationToken);
            
            await runService.HeartbeatAsync(
                run.Id,
                "persisting",
                "analysis-engine",
                executionId,
                cancellationToken);
            
            await persistenceService.PersistSuccessfulAsync(run, review, cancellationToken);
            
            logger.LogInformation(
                "Analysis completed. AnalysisId={AnalysisId} ExecutionId={ExecutionId} Attempt={Attempt}",
                run.Id,
                executionId,
                run.Attempt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Analysis execution interrupted. AnalysisId={AnalysisId} ExecutionId={ExecutionId}",
                run.Id,
                executionId);
            
            throw;
        }
        catch (Exception exception)
        {
            var retryable = IsRetryable(exception) && run.Attempt < 3;
            
            logger.LogError(
                exception,
                "Analysis failed. AnalysisId={AnalysisId} ExecutionId={ExecutionId} Attempt={Attempt} Retryable={Retryable}",
                run.Id,
                executionId,
                run.Attempt,
                retryable);
            
            await persistenceService.PersistFailureAsync(
                run,
                retryable ? "TRANSIENT_EXECUTION_FAILURE" : "ANALYSIS_FAILED",
                "The analysis execution failed. See the failure category and logs for details.",
                retryable,
                cancellationToken);

            if (retryable)
            {
                var retry = await runService.CreateRetryAsync(run, cancellationToken);
                
                var triggeredExecutionId = await executionTrigger.TriggerAsync(
                    retry.Id,
                    cancellationToken);
                
                if (triggeredExecutionId is not null)
                {
                    retry.CloudRunExecutionId = triggeredExecutionId;
                    retry.Stage = "submitted";
                    
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }

    private static bool IsRetryable(Exception exception) =>
        exception is TimeoutException
            or HttpRequestException
            or TaskCanceledException;
}
