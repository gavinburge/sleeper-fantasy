namespace SleeperFantasy.Domain.Providers;

public enum GameStatus
{
    Unknown,
    Active,
    Questionable,
    Doubtful,
    Out,
    InjuredReserve,
    Suspended
}

public sealed record InjuryReportRecord(
    string ProviderPlayerId,
    string PlayerName,
    string? Team,
    string? Position,
    string? Injury,
    string? PracticeStatus,
    GameStatus Status,
    DateTimeOffset ObservedAt,
    string SourceUrl);
