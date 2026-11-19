using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Domain.Providers;
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

    [Fact]
    public async Task Normalizes_mismatched_external_ids_before_analysis_consumes_performance_and_usage()
    {
        var fixture = new AnalysisFixture
        {
            ExternalRoster =
            [
                new ExternalPlayerRecord("gsis-rostered", "Rostered Player", "ABC", "RB", "gsis-rostered", DateTimeOffset.UtcNow),
                new ExternalPlayerRecord("gsis-free", "Free Player", "ABC", "WR", "gsis-free", DateTimeOffset.UtcNow)
            ],
            ExternalIds = new Dictionary<string, string>
            {
                ["rostered"] = "gsis-rostered",
                ["free"] = "gsis-free"
            }
        };

        var result = await fixture.CreateRefreshService().RefreshAsync(fixture.Configuration, CancellationToken.None);

        Assert.Contains(result.Performance, x => x.PlayerId == "rostered");
        Assert.Contains(result.Usage, x => x.ProviderPlayerId == "free");
        Assert.DoesNotContain(result.Performance, x => x.PlayerId.StartsWith("gsis-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deduplicates_external_roster_rows_before_identity_resolution()
    {
        var fixture = new AnalysisFixture
        {
            ExternalRoster =
            [
                new ExternalPlayerRecord("gsis-rostered", "Rostered Player", "ABC", "RB", "gsis-rostered", DateTimeOffset.UtcNow.AddMinutes(-1)),
                new ExternalPlayerRecord("gsis-rostered", "Rostered Player", "ABC", "RB", "gsis-rostered", DateTimeOffset.UtcNow)
            ],
            ExternalIds = new Dictionary<string, string>
            {
                ["rostered"] = "gsis-rostered",
                ["free"] = "gsis-rostered"
            }
        };

        var result = await fixture.CreateRefreshService().RefreshAsync(fixture.Configuration, CancellationToken.None);

        Assert.Contains(result.Performance, x => x.PlayerId == "rostered");
        Assert.Contains(result.Usage, x => x.ProviderPlayerId == "rostered");
        Assert.DoesNotContain(result.Evidence, x => x.EvidenceType == "identity-conflict");
    }

    [Fact]
    public async Task Keeps_unresolved_external_ids_out_of_claims_and_records_identity_metadata()
    {
        var fixture = new AnalysisFixture
        {
            ExternalRoster =
            [
                new ExternalPlayerRecord("gsis-a", "Unknown Player", "ABC", "WR", "gsis-a", DateTimeOffset.UtcNow)
            ],
            ExternalIds = new Dictionary<string, string> { ["rostered"] = "unresolved-id" }
        };

        var result = await fixture.CreateRefreshService().RefreshAsync(fixture.Configuration, CancellationToken.None);

        Assert.Empty(result.Performance);
        Assert.Contains(result.Evidence, x =>
            x.EvidenceType == "identity-unavailable"
            || x.EvidenceType == "identity-conflict");
    }

    [Fact]
    public async Task Mapped_ids_join_into_roster_risk_and_waiver_scores()
    {
        var fixture = new AnalysisFixture
        {
            ExternalRoster =
            [
                new ExternalPlayerRecord("gsis-rostered", "Rostered Player", "ABC", "RB", "gsis-rostered", DateTimeOffset.UtcNow),
                new ExternalPlayerRecord("gsis-free", "Free Player", "ABC", "WR", "gsis-free", DateTimeOffset.UtcNow)
            ],
            ExternalIds = new Dictionary<string, string>
            {
                ["rostered"] = "gsis-rostered",
                ["free"] = "gsis-free"
            }
        };
        var refreshed = await fixture.CreateRefreshService().RefreshAsync(fixture.Configuration, CancellationToken.None);
        var risk = new RosterRiskAnalyzer().Analyze(
            refreshed.League!, refreshed.Performance, refreshed.Schedule, refreshed.Injuries,
            fixture.Configuration.RosterId, refreshed.RefreshedAt);
        var availability = new AvailablePlayerService().Derive(
            refreshed.League!, refreshed.Failures, refreshed.RefreshedAt);
        var waivers = new WaiverRankingService().Rank(
            refreshed.League!, risk, availability, refreshed.Performance, refreshed.Usage,
            refreshed.Injuries, refreshed.RefreshedAt, refreshed.Evidence);

        Assert.Equal(10m, risk.Players.Single(x => x.PlayerId == "rostered").RecentPerformance);
        Assert.True(waivers.Recommendations.Single(x => x.PlayerId == "free").Score > 0m);
    }
}
