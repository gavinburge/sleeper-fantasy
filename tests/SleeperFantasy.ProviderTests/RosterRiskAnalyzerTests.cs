using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Domain.Providers;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class RosterRiskAnalyzerTests
{
    [Fact]
    public void Marks_bye_injury_and_missing_signals()
    {
        var snapshot = new AnalysisFixture().Snapshot with
        {
            Rosters = [new RosterRecord("roster-1", "sleeper-user", ["p1", "p2"], ["p1"], null, null, null, null)],
            Players = new Dictionary<string, PlayerRecord>
            {
                ["p1"] = new("p1", "Injured Player", null, null, "RB", "ABC", null, null, null, true),
                ["p2"] = new("p2", "Bye Player", null, null, "WR", "XYZ", null, null, null, true)
            }
        };
        var report = new RosterRiskAnalyzer().Analyze(
            snapshot,
            [new PlayerPerformanceRecord("p1", "ABC", "RB", "2026", 4,
                new Dictionary<string, decimal> { ["fantasy_points"] = 12m }, DateTimeOffset.UtcNow)],
            [
                new ScheduleRecord("g1", "2026", 5, null, "ABC", "DEF", null),
                new ScheduleRecord("g2", "2026", 6, null, "XYZ", "ABC", null)
            ],
            [new InjuryReportRecord("p1", "Injured Player", "ABC", "RB", "knee", "limited", GameStatus.Out, DateTimeOffset.UtcNow, "fixture")],
            "roster-1",
            DateTimeOffset.UtcNow);

        Assert.Contains("injury", report.Players.Single(x => x.PlayerId == "p1").RiskCodes);
        Assert.Contains("bye", report.Players.Single(x => x.PlayerId == "p2").RiskCodes);
        Assert.Contains("recent-performance", report.Players.Single(x => x.PlayerId == "p2").UnavailableSignals);
        Assert.Equal(12m, report.Players.Single(x => x.PlayerId == "p1").RecentPerformance);
    }

    [Fact]
    public void Reports_offseason_as_unavailable()
    {
        var snapshot = new AnalysisFixture().Snapshot with
        {
            State = new LeagueState("nfl", "2026", "0", "off", "0")
        };

        var report = new RosterRiskAnalyzer().Analyze(snapshot, [], [], [], "roster-1", DateTimeOffset.UtcNow);

        Assert.False(report.IsAvailable);
        Assert.Empty(report.Players);
        Assert.Contains("unavailable", report.Warnings.Single(), StringComparison.OrdinalIgnoreCase);
    }
}
