using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Providers;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class WaiverRankingServiceTests
{
    [Fact]
    public void Uses_scoring_settings_and_surfaces_conflicts()
    {
        var fixture = new AnalysisFixture();
        var availability = new AvailabilityPool(
            [new AvailablePlayer("free", "Free Player", "WR", "ABC", DateTimeOffset.UtcNow, ["WR"], true)],
            DateTimeOffset.UtcNow,
            true,
            []);
        var ranking = new WaiverRankingService().Rank(
            fixture.Snapshot,
            fixture.RosterRisk,
            availability,
            [new PlayerPerformanceRecord("free", "ABC", "WR", "2026", 4, new Dictionary<string, decimal> { ["receptions"] = 8 }, DateTimeOffset.UtcNow)],
            [],
            [],
            DateTimeOffset.UtcNow,
            [new EvidenceItem("performance-1", "fixture", "performance", "week-4", "performance", DateTimeOffset.UtcNow, null)]);

        var recommendation = Assert.Single(ranking.Recommendations);
        Assert.Equal(8m, recommendation.Score);
        Assert.Contains("performance-1", recommendation.EvidenceIds);
        Assert.Contains("scoring settings", recommendation.Reasons.Single(x => x.Contains("Scoring settings", StringComparison.OrdinalIgnoreCase)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assigns_priority_and_marks_incomplete_results_stale()
    {
        var fixture = new AnalysisFixture();
        var availability = new AvailabilityPool(
            [new AvailablePlayer("free", "Free Player", "WR", "ABC", DateTimeOffset.UtcNow, ["WR"], true)],
            DateTimeOffset.UtcNow,
            false,
            ["incomplete"]);

        var result = new WaiverRankingService().Rank(
            fixture.Snapshot,
            fixture.RosterRisk,
            availability,
            [new PlayerPerformanceRecord("free", "ABC", "WR", "2026", 4,
                new Dictionary<string, decimal> { ["fantasy_points"] = 1 }, DateTimeOffset.UtcNow)],
            [],
            [],
            DateTimeOffset.UtcNow);

        var recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(1, recommendation.Priority);
        Assert.True(recommendation.IsStale);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void Excludes_zero_score_players()
    {
        var fixture = new AnalysisFixture();
        var availability = new AvailabilityPool(
            [
                new AvailablePlayer("positive", "Positive Player", "WR", "ABC", DateTimeOffset.UtcNow, ["WR"], true),
                new AvailablePlayer("zero", "Zero Player", "WR", "ABC", DateTimeOffset.UtcNow, ["WR"], true)
            ],
            DateTimeOffset.UtcNow,
            true,
            []);

        var result = new WaiverRankingService().Rank(
            fixture.Snapshot,
            fixture.RosterRisk,
            availability,
            [new PlayerPerformanceRecord("positive", "ABC", "WR", "2026", 4,
                new Dictionary<string, decimal> { ["fantasy_points"] = 1 }, DateTimeOffset.UtcNow)],
            [],
            [],
            DateTimeOffset.UtcNow);

        var recommendation = Assert.Single(result.Recommendations);
        Assert.Equal("positive", recommendation.PlayerId);
        Assert.Equal(1, recommendation.Priority);
    }
}
