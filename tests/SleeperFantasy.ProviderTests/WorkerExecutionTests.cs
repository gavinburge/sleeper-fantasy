extern alias WorkerProject;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SleeperFantasy.Application.Features.Analysis;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class WorkerExecutionTests
{
    [Fact]
    public async Task Process_interruption_cancels_the_requested_run_and_stops_cleanly()
    {
        var runId = Guid.NewGuid();
        var executor = new BlockingExecutor();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["analysis-run-id"] = runId.ToString()
            });
        builder.Services.AddSingleton<IAnalysisRunExecutor>(executor);
        builder.Services.AddHostedService<WorkerProject::SleeperFantasy.Worker.Worker>();

        using var host = builder.Build();
        await host.StartAsync();
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await host.StopAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(runId, executor.RunId);
        Assert.True(executor.Cancelled);
    }

    private sealed class BlockingExecutor : IAnalysisRunExecutor
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid? RunId { get; private set; }
        public bool Cancelled { get; private set; }

        public async Task ExecuteAsync(
            Guid? analysisRunId,
            string? executionId,
            CancellationToken cancellationToken)
        {
            RunId = analysisRunId;
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled = true;
                throw;
            }
        }
    }
}
