using Google.Cloud.Run.V2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Infrastructure.Configuration;

namespace SleeperFantasy.Infrastructure.Providers;

public sealed class CloudRunAnalysisExecutionTrigger(
    IOptions<RuntimeOptions> runtimeOptions,
    ILogger<CloudRunAnalysisExecutionTrigger> logger) : IAnalysisExecutionTrigger
{
    public async System.Threading.Tasks.Task<string?> TriggerAsync(
        Guid analysisRunId,
        CancellationToken cancellationToken)
    {
        var jobName = runtimeOptions.Value.CloudRunJobName;
        if (string.IsNullOrWhiteSpace(jobName))
        {
            logger.LogInformation(
                "Analysis queued for local worker execution. AnalysisId={AnalysisId}",
                analysisRunId);
            return null;
        }

        var client = await JobsClient.CreateAsync(cancellationToken);
        var operation = await client.RunJobAsync(
            new RunJobRequest
            {
                Name = jobName,
                Overrides = new RunJobRequest.Types.Overrides
                {
                    ContainerOverrides =
                    {
                        new RunJobRequest.Types.Overrides.Types.ContainerOverride
                        {
                            Args = { "--analysis-run-id", analysisRunId.ToString() }
                        }
                    }
                }
            },
            cancellationToken);
        logger.LogInformation(
            "Cloud Run execution requested. AnalysisId={AnalysisId} Job={JobName} OperationId={OperationId}",
            analysisRunId,
            jobName,
            operation.Name);
        return operation.Name;
    }
}
