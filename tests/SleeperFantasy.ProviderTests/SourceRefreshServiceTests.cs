using SleeperFantasy.Application.Features.Analysis;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class SourceRefreshServiceTests
{
    [Fact]
    public async Task Builds_snapshot_evidence_and_partial_failure_metadata()
    {
        var fixture = new AnalysisFixture();
        var result = await fixture.CreateRefreshService(failInjuries: true)
            .RefreshAsync(fixture.Configuration, CancellationToken.None);

        Assert.NotNull(result.League);
        Assert.Contains(result.Evidence, x => x.EvidenceType == "league-settings");
        Assert.Contains(result.Failures, x => x.Provider == "fixture-injury");
        Assert.True(result.IsPartial);
        Assert.True(result.IsUnavailable);
        Assert.Contains(result.Performance, x => x.Week == 4);
    }

    [Fact]
    public async Task Converts_provider_exceptions_to_source_failure_metadata()
    {
        var fixture = new AnalysisFixture();
        var result = await fixture.CreateRefreshService(throwInjuries: true)
            .RefreshAsync(fixture.Configuration, CancellationToken.None);

        var failure = Assert.Single(result.Failures, x => x.Provider == "fixture-injury");
        Assert.Equal("PROVIDER_EXCEPTION", failure.ErrorCode);
        Assert.Contains("fixture exception", failure.ErrorMessage);
    }
}
