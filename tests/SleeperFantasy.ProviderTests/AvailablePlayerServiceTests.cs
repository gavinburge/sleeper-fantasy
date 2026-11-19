using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Providers;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class AvailablePlayerServiceTests
{
    [Fact]
    public void Excludes_rostered_and_unsupported_records()
    {
        var fixture = new AnalysisFixture();
        var snapshot = fixture.Snapshot with
        {
            Players = new Dictionary<string, PlayerRecord>
            {
                ["rostered"] = new("rostered", "Rostered", null, null, "RB", "ABC", null, null, null, true),
                ["free"] = fixture.FreePlayer,
                ["def"] = new("def", "Defense", null, null, "DEF", "ABC", null, null, null, true),
                ["invalid"] = new("invalid", "", null, null, "RB", "ABC", null, null, null, true)
            }
        };

        var pool = new AvailablePlayerService().Derive(snapshot, [], DateTimeOffset.UtcNow);

        Assert.True(pool.IsComplete);
        Assert.Equal("free", Assert.Single(pool.Players).PlayerId);
    }

    [Fact]
    public void Marks_incomplete_roster_snapshots()
    {
        var fixture = new AnalysisFixture();
        var snapshot = fixture.Snapshot with { Rosters = [] };
        var failures = new[]
        {
            new SourceFailure("Sleeper", ProviderAvailability.Unavailable, DateTimeOffset.UtcNow, "ROSTER_UNAVAILABLE", "fixture")
        };

        var result = new AvailablePlayerService().Derive(snapshot, failures, DateTimeOffset.UtcNow);

        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Players.Count);
        Assert.All(result.Players, player => Assert.False(player.EvidenceComplete));
        Assert.Contains("incomplete", result.Warnings.Single(), StringComparison.OrdinalIgnoreCase);
    }
}
