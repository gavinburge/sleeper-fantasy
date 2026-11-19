namespace SleeperFantasy.Domain.Providers;

public interface ILeagueDataProvider
{
    string ProviderName { get; }
    Task<ProviderResult<SleeperUser>> GetUserAsync(string username, CancellationToken cancellationToken);
    Task<ProviderResult<IReadOnlyList<LeagueSummary>>> GetLeaguesAsync(string userId, string season, CancellationToken cancellationToken);
    Task<ProviderResult<LeagueSummary>> GetLeagueAsync(string leagueId, CancellationToken cancellationToken);
    Task<ProviderResult<IReadOnlyList<RosterRecord>>> GetRostersAsync(string leagueId, CancellationToken cancellationToken);
    Task<ProviderResult<LeagueSettings>> GetSettingsAsync(string leagueId, CancellationToken cancellationToken);
    Task<ProviderResult<IReadOnlyList<MatchupRecord>>> GetMatchupsAsync(string leagueId, int week, CancellationToken cancellationToken);
    Task<ProviderResult<IReadOnlyList<TransactionRecord>>> GetTransactionsAsync(string leagueId, int round, CancellationToken cancellationToken);
    Task<ProviderResult<IReadOnlyDictionary<string, PlayerRecord>>> GetPlayersAsync(CancellationToken cancellationToken);
    Task<ProviderResult<LeagueState>> GetStateAsync(CancellationToken cancellationToken);
    Task<ProviderResult<IReadOnlyList<TrendingPlayer>>> GetTrendingPlayersAsync(string sport, string type, int lookbackHours, CancellationToken cancellationToken);
}

public interface IPerformanceDataProvider
{
    string ProviderName { get; }
    Task<ProviderResult<IReadOnlyList<ScheduleRecord>>> GetScheduleAsync(string season, CancellationToken cancellationToken);
    Task<ProviderResult<IReadOnlyList<PlayerPerformanceRecord>>> GetWeeklyPlayerStatsAsync(string season, int week, CancellationToken cancellationToken);
    Task<ProviderResult<IReadOnlyList<ExternalPlayerRecord>>> GetRostersAsync(string season, CancellationToken cancellationToken);
    Task<ProviderResult<IReadOnlyList<PlayerUsageRecord>>> GetUsageAsync(string season, CancellationToken cancellationToken);
}

public interface IInjuryDataProvider
{
    string ProviderName { get; }
    Task<ProviderResult<IReadOnlyList<InjuryReportRecord>>> GetCurrentReportsAsync(CancellationToken cancellationToken);
}

public interface ILanguageModelProvider
{
    string ProviderName { get; }
    Task<ProviderResult<LanguageModelResponse>> AnalyzeAsync(
        LanguageModelRequest request,
        CancellationToken cancellationToken);
}
