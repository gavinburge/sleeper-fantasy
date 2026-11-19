using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Domain.Providers;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class AnalysisOrchestratorTests
{
    [Fact]
    public async Task Runs_refresh_deterministic_analysis_and_model_merge()
    {
        var fixture = new AnalysisFixture();
        var model = new StubLanguageModelProvider
        {
            Response = new LanguageModelResponse("fixture-model", "done", [], [], DateTimeOffset.UtcNow)
        };
        var orchestrator = new AnalysisOrchestrator(
            fixture.CreateRefreshService(),
            new RosterRiskAnalyzer(),
            new AvailablePlayerService(),
            new WaiverRankingService(),
            new LanguageModelAnalysisService(model));

        var result = await orchestrator.AnalyzeAsync(fixture.Configuration, CancellationToken.None);

        Assert.True(result.ContextualAnalysisAvailable);
        Assert.NotNull(result.Deterministic.RosterRisk);
        Assert.NotNull(result.Deterministic.Availability);
        Assert.NotNull(result.Deterministic.Waivers);
        Assert.NotEmpty(model.Requests);
    }
}
