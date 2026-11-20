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

    [Fact]
    public void Limits_shortlist_to_ten_recommendations_per_position()
    {
        var fixture = new AnalysisFixture();
        var players = Enumerable.Range(1, 11)
            .Select(index => new AvailablePlayer(
                $"rb-{index}",
                $"Running Back {index}",
                "RB",
                "ABC",
                DateTimeOffset.UtcNow,
                ["RB"],
                true))
            .ToArray();
        var availability = new AvailabilityPool(players, DateTimeOffset.UtcNow, true, []);
        var performance = players
            .Select((player, index) => new PlayerPerformanceRecord(
                player.PlayerId,
                "ABC",
                "RB",
                "2026",
                4,
                new Dictionary<string, decimal> { ["fantasy_points"] = 10m - index },
                DateTimeOffset.UtcNow))
            .ToArray();

        var result = new WaiverRankingService().Rank(
            fixture.Snapshot,
            fixture.RosterRisk,
            availability,
            performance,
            [],
            [],
            DateTimeOffset.UtcNow);

        Assert.Equal(10, result.Recommendations.Count);
        Assert.Equal(
            Enumerable.Range(1, 10).Select(index => $"rb-{index}"),
            result.Recommendations.Select(x => x.PlayerId));
        Assert.Equal(Enumerable.Range(1, 10), result.Recommendations.Select(x => x.Priority));
    }
}
