using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Entities;
using SleeperFantasy.Domain.Providers;

namespace SleeperFantasy.ProviderTests;

internal sealed class AnalysisFixture
{
    public readonly ManagerConfiguration Configuration = new()
    {
        Id = Guid.NewGuid(),
        UserId = "app-user",
        SleeperUsername = "manager",
        SleeperUserId = "sleeper-user",
        LeagueId = "league-1",
        TeamName = "Home",
        RosterId = "roster-1",
        Season = 2026,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public readonly PlayerRecord FreePlayer =
        new("free", "Free Player", null, null, "WR", "ABC", null, null, null, true);

    public LeagueSnapshot Snapshot => new(
        new LeagueSummary("league-1", "League", "2026", "in_season", "nfl", 2, null),
        new LeagueSettings(
            "league-1",
            new Dictionary<string, decimal> { ["rec"] = 1m },
            new Dictionary<string, int> { ["RB"] = 1, ["WR"] = 2 },
            5,
            "in_season"),
        [new RosterRecord("roster-1", "sleeper-user", ["rostered"], ["rostered"], null, null, null, null)],
        [],
        [],
        new LeagueState("nfl", "2026", "5", "regular", "5"),
        new Dictionary<string, PlayerRecord>
        {
            ["rostered"] = new("rostered", "Rostered Player", null, null, "RB", "ABC", null, null, null, true),
            ["free"] = FreePlayer
        },
        []);

    public RosterRiskReport RosterRisk =>
        new("regular", 5, [], [], true, DateTimeOffset.UtcNow);

    public EvidenceItem Evidence =>
        new("evidence-1", "fixture", "player", "free", "Free Player has opportunity.", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), 0.9m, "free");

    public DeterministicAnalysis Deterministic =>
        new(RosterRisk, new AvailabilityPool([], DateTimeOffset.UtcNow, true, []),
            new WaiverRanking([], true, [], DateTimeOffset.UtcNow), [Evidence], [], DateTimeOffset.UtcNow);

    public AnalysisReview Review =>
        new(Deterministic, null, false, ["model unavailable"], 0.9m, false, DateTimeOffset.UtcNow);

    public SourceRefreshService CreateRefreshService(bool failInjuries = false, bool throwInjuries = false) =>
        new(new StubLeagueProvider(this), new StubPerformanceProvider(), new StubInjuryProvider
        {
            Fail = failInjuries,
            Throw = throwInjuries
        });
}

internal sealed class StubLeagueProvider(AnalysisFixture fixture) : ILeagueDataProvider
{
    public string ProviderName => "fixture-league";

    public Task<ProviderResult<SleeperUser>> GetUserAsync(string username, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<SleeperUser>.AvailableResult(
            new SleeperUser(fixture.Configuration.SleeperUserId, username, null), ProviderName, DateTimeOffset.UtcNow));

    public Task<ProviderResult<IReadOnlyList<LeagueSummary>>> GetLeaguesAsync(string userId, string season, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<IReadOnlyList<LeagueSummary>>.AvailableResult([fixture.Snapshot.League], ProviderName, DateTimeOffset.UtcNow));

    public Task<ProviderResult<LeagueSummary>> GetLeagueAsync(string leagueId, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<LeagueSummary>.AvailableResult(fixture.Snapshot.League, ProviderName, DateTimeOffset.UtcNow));

    public Task<ProviderResult<IReadOnlyList<RosterRecord>>> GetRostersAsync(string leagueId, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<IReadOnlyList<RosterRecord>>.AvailableResult(fixture.Snapshot.Rosters, ProviderName, DateTimeOffset.UtcNow));

    public Task<ProviderResult<LeagueSettings>> GetSettingsAsync(string leagueId, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<LeagueSettings>.AvailableResult(fixture.Snapshot.Settings, ProviderName, DateTimeOffset.UtcNow));

    public Task<ProviderResult<IReadOnlyList<MatchupRecord>>> GetMatchupsAsync(string leagueId, int week, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<IReadOnlyList<MatchupRecord>>.EmptyResult(ProviderName, DateTimeOffset.UtcNow));

    public Task<ProviderResult<IReadOnlyList<TransactionRecord>>> GetTransactionsAsync(string leagueId, int round, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<IReadOnlyList<TransactionRecord>>.EmptyResult(ProviderName, DateTimeOffset.UtcNow));

    public Task<ProviderResult<IReadOnlyDictionary<string, PlayerRecord>>> GetPlayersAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<IReadOnlyDictionary<string, PlayerRecord>>.AvailableResult(fixture.Snapshot.Players, ProviderName, DateTimeOffset.UtcNow));

    public Task<ProviderResult<LeagueState>> GetStateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<LeagueState>.AvailableResult(fixture.Snapshot.State, ProviderName, DateTimeOffset.UtcNow));

    public Task<ProviderResult<IReadOnlyList<TrendingPlayer>>> GetTrendingPlayersAsync(string sport, string type, int lookbackHours, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<IReadOnlyList<TrendingPlayer>>.EmptyResult(ProviderName, DateTimeOffset.UtcNow));
}

internal sealed class StubPerformanceProvider : IPerformanceDataProvider
{
    public string ProviderName => "fixture-performance";

    public Task<ProviderResult<IReadOnlyList<ScheduleRecord>>> GetScheduleAsync(string season, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<IReadOnlyList<ScheduleRecord>>.AvailableResult([], ProviderName, DateTimeOffset.UtcNow));

    public Task<ProviderResult<IReadOnlyList<PlayerPerformanceRecord>>> GetWeeklyPlayerStatsAsync(string season, int week, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<IReadOnlyList<PlayerPerformanceRecord>>.AvailableResult(
            week > 0
                ? [new PlayerPerformanceRecord("rostered", "ABC", "RB", season, week,
                    new Dictionary<string, decimal> { ["fantasy_points"] = 10m }, DateTimeOffset.UtcNow)]
                : [],
            ProviderName,
            DateTimeOffset.UtcNow));

    public Task<ProviderResult<IReadOnlyList<ExternalPlayerRecord>>> GetRostersAsync(string season, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<IReadOnlyList<ExternalPlayerRecord>>.AvailableResult([], ProviderName, DateTimeOffset.UtcNow));

    public Task<ProviderResult<IReadOnlyList<PlayerUsageRecord>>> GetUsageAsync(string season, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<IReadOnlyList<PlayerUsageRecord>>.AvailableResult([], ProviderName, DateTimeOffset.UtcNow));
}

internal sealed class StubInjuryProvider : IInjuryDataProvider
{
    public bool Fail { get; init; }
    public bool Throw { get; init; }
    public string ProviderName => "fixture-injury";

    public Task<ProviderResult<IReadOnlyList<InjuryReportRecord>>> GetCurrentReportsAsync(CancellationToken cancellationToken) =>
        Throw
            ? throw new InvalidOperationException("fixture exception")
            :
        Task.FromResult(Fail
            ? ProviderResult<IReadOnlyList<InjuryReportRecord>>.Failure(
                ProviderAvailability.Unavailable, ProviderName, DateTimeOffset.UtcNow, "INJURY_UNAVAILABLE", "fixture")
            : ProviderResult<IReadOnlyList<InjuryReportRecord>>.AvailableResult([], ProviderName, DateTimeOffset.UtcNow));
}

internal sealed class StubLanguageModelProvider : ILanguageModelProvider
{
    public string ProviderName => "fixture-model";
    public LanguageModelResponse? Response { get; init; }
    public List<LanguageModelRequest> Requests { get; } = [];

    public Task<ProviderResult<LanguageModelResponse>> AnalyzeAsync(
        LanguageModelRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(ProviderResult<LanguageModelResponse>.AvailableResult(
            Response!, ProviderName, DateTimeOffset.UtcNow));
    }
}
