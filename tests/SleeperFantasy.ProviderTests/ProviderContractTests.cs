using System.Text.Json;
using SleeperFantasy.Application.Providers;
using SleeperFantasy.Application.Features.LeagueSetup;
using SleeperFantasy.Application.Abstractions;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class ProviderContractTests
{
    [Fact]
    public async Task Provider_interfaces_can_be_substituted_with_fixture_adapter()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "sleeper-user.json")));
        var user = fixture.RootElement;
        ILeagueDataProvider provider = new FixtureLeagueProvider
        {
            User = new SleeperUser(
                user.GetProperty("user_id").GetString()!,
                user.GetProperty("username").GetString()!,
                user.GetProperty("display_name").GetString())
        };

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

    [Fact]
    public async Task League_discovery_returns_leagues_and_manager_roster()
    {
        var provider = new FixtureLeagueProvider
        {
            User = new SleeperUser("user-1", "manager", "Manager"),
            Leagues =
            [
                new LeagueSummary("league-1", "League", "2026", "in_season", "nfl", 2, "ppr")
            ],
            Rosters =
            [
                new RosterRecord("1", "user-1", [], [], null, null, null, null, "Home"),
                new RosterRecord("2", "user-2", [], [], null, null, null, null, "Rivals")
            ]
        };
        var service = new LeagueConfigurationService(provider, new TestDbContext());

        var result = await service.DiscoverAsync("manager", 2026, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Value!.Leagues);
        Assert.True(result.Value.Leagues[0].Teams[0].IsManagerTeam);
    }

    [Fact]
    public async Task League_discovery_returns_not_found_for_empty_leagues()
    {
        var service = new LeagueConfigurationService(
            new FixtureLeagueProvider
            {
                User = new SleeperUser("user-1", "manager", "Manager"),
                Leagues = []
            },
            new TestDbContext());

        var result = await service.DiscoverAsync("manager", 2026, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("NO_LEAGUES", result.ErrorCode);
    }

    [Fact]
    public async Task League_save_rejects_ambiguous_team_name()
    {
        var provider = new FixtureLeagueProvider
        {
            User = new SleeperUser("user-1", "manager", "Manager"),
            Leagues = [new LeagueSummary("league-1", "League", "2026", "in_season", "nfl", 2, "ppr")],
            Rosters =
            [
                new RosterRecord("1", "user-1", [], [], null, null, null, null, "Same Name"),
                new RosterRecord("2", "user-1", [], [], null, null, null, null, "Same Name")
            ]
        };
        var service = new LeagueConfigurationService(provider, new TestDbContext());

        var result = await service.SaveAsync(
            "app-user",
            new LeagueConfigurationRequest("manager", 2026, "league-1", null, "Same Name"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("AMBIGUOUS_TEAM", result.ErrorCode);
    }

    [Fact]
    public async Task League_configuration_round_trips_without_sleeper_credentials()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var provider = new FixtureLeagueProvider
        {
            User = new SleeperUser("user-1", "manager", "Manager"),
            Leagues = [new LeagueSummary("league-1", "League", "2026", "in_season", "nfl", 2, "ppr")],
            Rosters = [new RosterRecord("1", "user-1", [], [], null, null, null, null, "Home")]
        };
        var service = new LeagueConfigurationService(provider, db);

        var saved = await service.SaveAsync(
            "app-user",
            new LeagueConfigurationRequest("manager", 2026, "league-1", "1", null),
            CancellationToken.None);
        var loaded = await service.GetAsync("app-user", CancellationToken.None);

        Assert.True(saved.Succeeded);
        Assert.NotNull(loaded);
        Assert.Equal("user-1", loaded.SleeperUserId);
        Assert.Equal("league-1", loaded.LeagueId);
        Assert.Equal("1", loaded.RosterId);
        Assert.Equal(2026, loaded.Season);
    }

    private sealed class FixtureLeagueProvider : ILeagueDataProvider
    {
        public SleeperUser User { get; init; } = new("fixture-user-id", "fixture-user", "Fixture User");
        public IReadOnlyList<LeagueSummary> Leagues { get; init; } = [];
        public IReadOnlyList<RosterRecord> Rosters { get; init; } = [];
        public string ProviderName => "fixture";

        public Task<ProviderResult<SleeperUser>> GetUserAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<SleeperUser>.AvailableResult(
                User with { Username = username },
                ProviderName,
                DateTimeOffset.UtcNow));

        public Task<ProviderResult<IReadOnlyList<LeagueSummary>>> GetLeaguesAsync(string userId, string season, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<IReadOnlyList<LeagueSummary>>.AvailableResult(Leagues, ProviderName, DateTimeOffset.UtcNow));

        public Task<ProviderResult<LeagueSummary>> GetLeagueAsync(string leagueId, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<LeagueSummary>.Failure(ProviderAvailability.Unavailable, ProviderName, DateTimeOffset.UtcNow, "FIXTURE", "not needed"));

        public Task<ProviderResult<IReadOnlyList<RosterRecord>>> GetRostersAsync(string leagueId, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<IReadOnlyList<RosterRecord>>.AvailableResult(Rosters, ProviderName, DateTimeOffset.UtcNow));

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

    private sealed class TestDbContext : IAppDbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<SleeperFantasy.Domain.Entities.ManagerConfiguration> ManagerConfigurations =>
            throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<SleeperFantasy.Domain.Entities.ProviderCacheMetadata> ProviderCacheMetadata =>
            throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<SleeperFantasy.Domain.Entities.PlayerIdentityMapping> PlayerIdentityMappings =>
            throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<SleeperFantasy.Domain.Entities.AnalysisRun> AnalysisRuns =>
            throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<SleeperFantasy.Domain.Entities.AnalysisEvidence> AnalysisEvidence =>
            throw new NotSupportedException();
        public Microsoft.EntityFrameworkCore.DbSet<SleeperFantasy.Domain.Entities.AnalysisResult> AnalysisResults =>
            throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
