using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Domain.Analysis;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class AnalysisReviewLimiterTests
{
    [Fact]
    public void Limits_existing_reviews_to_three_recommendations_per_position()
    {
        var now = DateTimeOffset.UtcNow;
        var evidence = Enumerable.Range(1, 20)
            .Select(index => new EvidenceItem(
                $"evidence-{index}",
                "fixture",
                "performance",
                "player",
                "fact",
                now,
                null))
            .ToArray();
        var recommendations = Enumerable.Range(1, 4)
            .Select(index => new WaiverRecommendation(
                $"rb-{index}",
                $"Running Back {index}",
                "RB",
                100m - index,
                index,
                [],
                index == 1 ? evidence.Select(x => x.EvidenceId).ToArray() : [],
                [],
                0.5m,
                false))
            .Append(new WaiverRecommendation(
                "wr-1",
                "Wide Receiver 1",
                "WR",
                50m,
                5,
                [],
                [],
                [],
                0.5m,
                false))
            .ToArray();
        var availability = recommendations
            .Select(x => new AvailablePlayer(
                x.PlayerId,
                x.PlayerName,
                x.Position,
                "ABC",
                now,
                [x.Position!],
                true))
            .ToArray();
        var deterministic = new DeterministicAnalysis(
            new RosterRiskReport("regular", 1, [], [], true, now),
            new AvailabilityPool(availability, now, true, []),
            new WaiverRanking(recommendations, true, [], now),
            evidence,
            [],
            now);
        var review = new AnalysisReview(deterministic, null, false, [], 0.5m, false, now);

        var limited = AnalysisReviewLimiter.Limit(review);

        Assert.Equal(["rb-1", "rb-2", "rb-3", "wr-1"], limited.Deterministic.Waivers.Recommendations.Select(x => x.PlayerId));
        Assert.Equal(["rb-1", "rb-2", "rb-3", "wr-1"], limited.Deterministic.Availability.Players.Select(x => x.PlayerId));
        Assert.Equal([1, 2, 3, 4], limited.Deterministic.Waivers.Recommendations.Select(x => x.Priority));
        Assert.Equal(10, limited.Deterministic.Evidence.Count);
        Assert.Equal(10, limited.Deterministic.Waivers.Recommendations[0].EvidenceIds.Count);
    }

    [Fact]
    public void Limits_reviews_to_twenty_recommendations_overall()
    {
        var now = DateTimeOffset.UtcNow;
        var recommendations = Enumerable.Range(1, 30)
            .Select(index => new WaiverRecommendation(
                $"player-{index}",
                $"Player {index}",
                $"POS-{index}",
                100m - index,
                index,
                [],
                [],
                [],
                0.5m,
                false))
            .ToArray();
        var availability = recommendations
            .Select(x => new AvailablePlayer(
                x.PlayerId,
                x.PlayerName,
                x.Position,
                "ABC",
                now,
                [x.Position!],
                true))
            .ToArray();
        var deterministic = new DeterministicAnalysis(
            new RosterRiskReport("regular", 1, [], [], true, now),
            new AvailabilityPool(availability, now, true, []),
            new WaiverRanking(recommendations, true, [], now),
            [],
            [],
            now);
        var review = new AnalysisReview(deterministic, null, false, [], 0.5m, false, now);

        var limited = AnalysisReviewLimiter.Limit(review);

        Assert.Equal(20, limited.Deterministic.Waivers.Recommendations.Count);
        Assert.Equal(20, limited.Deterministic.Availability.Players.Count);
    }

    [Fact]
    public void Limits_large_evidence_payloads_for_transport()
    {
        var now = DateTimeOffset.UtcNow;
        var evidence = new EvidenceItem(
            "large-evidence",
            "fixture",
            "performance",
            "snapshot",
            new string('x', 5_000),
            now,
            null);
        var recommendation = new WaiverRecommendation(
            "player-1",
            "Player 1",
            "RB",
            1m,
            1,
            [],
            [evidence.EvidenceId],
            [],
            0.5m,
            false);
        var deterministic = new DeterministicAnalysis(
            new RosterRiskReport("regular", 1, [], [], true, now),
            new AvailabilityPool(
                [new AvailablePlayer("player-1", "Player 1", "RB", "ABC", now, ["RB"], true)],
                now,
                true,
                []),
            new WaiverRanking([recommendation], true, [], now),
            [evidence],
            [],
            now);
        var review = new AnalysisReview(deterministic, null, false, [], 0.5m, false, now);

        var limited = AnalysisReviewLimiter.LimitForTransport(review);

        Assert.DoesNotContain(new string('x', 5_000), limited.Deterministic.Evidence[0].Fact);
        Assert.Contains("omitted", limited.Deterministic.Evidence[0].Fact);
    }
}
