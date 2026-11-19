using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Domain.Providers;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class LanguageModelAnalysisServiceTests
{
    [Fact]
    public async Task Filters_unknown_evidence_and_clamps_confidence()
    {
        var model = new StubLanguageModelProvider
        {
            Response = new LanguageModelResponse(
                "fixture-model",
                "summary",
                [new LanguageModelFinding("free", "explanation", 2m, ["evidence-1", "unknown-evidence"], ["caveat"])],
                [],
                DateTimeOffset.UtcNow)
        };

        var review = await new LanguageModelAnalysisService(model)
            .MergeAsync("analysis", new AnalysisFixture().Deterministic, CancellationToken.None);

        var finding = Assert.Single(review.ContextualAnalysis!.Findings);
        Assert.Equal(["evidence-1"], finding.EvidenceIds);
        Assert.Equal(1m, finding.Confidence);
        Assert.True(review.ContextualAnalysisAvailable);
    }

    [Fact]
    public async Task Preserves_deterministic_findings_when_model_fails()
    {
        var fixture = new AnalysisFixture();
        var deterministic = fixture.Deterministic;
        var review = await new LanguageModelAnalysisService(new FailingLanguageModelProvider())
            .MergeAsync("analysis", deterministic, CancellationToken.None);

        Assert.False(review.ContextualAnalysisAvailable);
        Assert.Same(deterministic, review.Deterministic);
        Assert.Contains("unavailable", review.Caveats.Single(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FailingLanguageModelProvider : ILanguageModelProvider
    {
        public string ProviderName => "fixture-model";

        public Task<ProviderResult<LanguageModelResponse>> AnalyzeAsync(
            LanguageModelRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<LanguageModelResponse>.Failure(
                ProviderAvailability.Unavailable, ProviderName, DateTimeOffset.UtcNow, "UNAVAILABLE", "fixture"));
    }
}
