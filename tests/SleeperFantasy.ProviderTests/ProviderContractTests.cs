using SleeperFantasy.Application.Providers;
using SleeperFantasy.Domain.Providers;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class ProviderContractTests
{
    [Fact]
    public async Task Provider_interfaces_can_be_substituted_with_fixture_adapter()
    {
        ILeagueDataProvider provider = new FixtureLeagueProvider();

        var result = await provider.GetUserAsync("fixture-user", CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal("fixture-user-id", result.Value?.UserId);
    }

    [Fact]
    public void Ambiguous_identity_matches_are_not_guessed()
    {
        var resolver = new PlayerIdentityResolver();
        var player = new PlayerRecord("slp-1", "Jordan Smith", null, null, "WR", "ABC", null, null, null, true);
        var candidates = new[]
        {
            new ExternalPlayerRecord("ext-1", "Jordan Smith", "ABC", "WR", null, DateTimeOffset.UtcNow),
            new ExternalPlayerRecord("ext-2", "Jordan Smith", "ABC", "WR", null, DateTimeOffset.UtcNow)
        };

        var match = resolver.Resolve(player, candidates, "fixture");

        Assert.True(match.IsAmbiguous);
        Assert.Null(match.ProviderPlayerId);
        Assert.NotNull(match.Warning);
    }

    [Fact]
    public void Stable_identity_match_resolves_without_name_guessing()
    {
        var resolver = new PlayerIdentityResolver();
        var player = new PlayerRecord("gsis-1", "Jordan Smith", null, null, "WR", "ABC", null, null, null, true);
        var candidates = new[]
        {
            new ExternalPlayerRecord("gsis-1", "Different Display Name", "XYZ", "QB", "gsis-1", DateTimeOffset.UtcNow)
        };

        var match = resolver.Resolve(player, candidates, "fixture");

        Assert.False(match.IsAmbiguous);
        Assert.Equal("gsis-1", match.ProviderPlayerId);
        Assert.Equal(1m, match.Confidence);
    }

    [Fact]
    public void Provider_result_preserves_rate_limit_and_unavailable_states()
    {
        var rateLimited = ProviderResult<string>.Failure(
            ProviderAvailability.RateLimited,
            "fixture",
            DateTimeOffset.UtcNow,
            "RATE_LIMITED",
            "retry later");
        var unavailable = ProviderResult<string>.Failure(
            ProviderAvailability.Unavailable,
            "fixture",
            DateTimeOffset.UtcNow,
            "UNAVAILABLE",
            "source down");

        Assert.False(rateLimited.IsSuccessful);
        Assert.Equal(ProviderAvailability.RateLimited, rateLimited.Availability);
        Assert.Equal(ProviderAvailability.Unavailable, unavailable.Availability);
    }

    private sealed class FixtureLeagueProvider : ILeagueDataProvider
    {
        public string ProviderName => "fixture";

        public Task<ProviderResult<SleeperUser>> GetUserAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<SleeperUser>.AvailableResult(
                new SleeperUser("fixture-user-id", username, "Fixture User"),
                ProviderName,
                DateTimeOffset.UtcNow));

        public Task<ProviderResult<IReadOnlyList<LeagueSummary>>> GetLeaguesAsync(string userId, string season, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<IReadOnlyList<LeagueSummary>>.EmptyResult(ProviderName, DateTimeOffset.UtcNow));

        public Task<ProviderResult<LeagueSummary>> GetLeagueAsync(string leagueId, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<LeagueSummary>.Failure(ProviderAvailability.Unavailable, ProviderName, DateTimeOffset.UtcNow, "FIXTURE", "not needed"));

        public Task<ProviderResult<IReadOnlyList<RosterRecord>>> GetRostersAsync(string leagueId, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<IReadOnlyList<RosterRecord>>.EmptyResult(ProviderName, DateTimeOffset.UtcNow));

        public Task<ProviderResult<LeagueSettings>> GetSettingsAsync(string leagueId, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<LeagueSettings>.Failure(ProviderAvailability.Unavailable, ProviderName, DateTimeOffset.UtcNow, "FIXTURE", "not needed"));

        public Task<ProviderResult<IReadOnlyList<MatchupRecord>>> GetMatchupsAsync(string leagueId, int week, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<IReadOnlyList<MatchupRecord>>.EmptyResult(ProviderName, DateTimeOffset.UtcNow));

        public Task<ProviderResult<IReadOnlyList<TransactionRecord>>> GetTransactionsAsync(string leagueId, int round, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<IReadOnlyList<TransactionRecord>>.EmptyResult(ProviderName, DateTimeOffset.UtcNow));

        public Task<ProviderResult<IReadOnlyDictionary<string, PlayerRecord>>> GetPlayersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<IReadOnlyDictionary<string, PlayerRecord>>.EmptyResult(ProviderName, DateTimeOffset.UtcNow));

        public Task<ProviderResult<LeagueState>> GetStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<LeagueState>.Failure(ProviderAvailability.Unavailable, ProviderName, DateTimeOffset.UtcNow, "FIXTURE", "not needed"));

        public Task<ProviderResult<IReadOnlyList<TrendingPlayer>>> GetTrendingPlayersAsync(string sport, string type, int lookbackHours, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<IReadOnlyList<TrendingPlayer>>.EmptyResult(ProviderName, DateTimeOffset.UtcNow));
    }
}
