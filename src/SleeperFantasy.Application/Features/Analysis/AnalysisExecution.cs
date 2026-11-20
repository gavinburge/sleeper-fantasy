namespace SleeperFantasy.Application.Features.Analysis;

public interface IAnalysisExecutionTrigger
{
    Task<string?> TriggerAsync(Guid analysisRunId, CancellationToken cancellationToken);
}

public interface IAnalysisRunExecutor
{
    Task ExecuteAsync(Guid? analysisRunId, string? executionId, CancellationToken cancellationToken);
}
