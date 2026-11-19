namespace SleeperFantasy.Domain.Providers;

public sealed record ScheduleRecord(
    string GameId,
    string Season,
    int Week,
    string? GameDate,
    string? HomeTeam,
    string? AwayTeam,
    string? Status);

public sealed record PlayerPerformanceRecord(
    string PlayerId,
    string? Team,
    string? Position,
    string Season,
    int Week,
    IReadOnlyDictionary<string, decimal> Statistics,
    DateTimeOffset ObservedAt);

public sealed record ExternalPlayerRecord(
    string ProviderPlayerId,
    string? FullName,
    string? Team,
    string? Position,
    string? GsisId,
    DateTimeOffset ObservedAt);

public sealed record PlayerUsageRecord(
    string ProviderPlayerId,
    string? Team,
    string? Position,
    IReadOnlyDictionary<string, decimal> Usage,
    DateTimeOffset ObservedAt);
