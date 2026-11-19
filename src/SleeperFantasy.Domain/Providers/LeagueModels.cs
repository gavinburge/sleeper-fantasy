namespace SleeperFantasy.Domain.Providers;

public sealed record SleeperUser(
    string UserId,
    string Username,
    string? DisplayName);

public sealed record LeagueSummary(
    string LeagueId,
    string Name,
    string Season,
    string Status,
    string Sport,
    int TotalRosters,
    string? ScoringType);

public sealed record LeagueSettings(
    string LeagueId,
    IReadOnlyDictionary<string, decimal> Values,
    IReadOnlyDictionary<string, int> RosterPositions,
    int CurrentWeek,
    string State);

public sealed record RosterRecord(
    string RosterId,
    string OwnerUserId,
    IReadOnlyList<string> PlayerIds,
    IReadOnlyList<string> StarterIds,
    int? Wins,
    int? Losses,
    int? Ties,
    decimal? PointsFor,
    string? TeamName = null);

public sealed record MatchupRecord(
    string MatchupId,
    string RosterId,
    int Week,
    decimal? Points,
    string? MatchupPeriod);

public sealed record TransactionRecord(
    string TransactionId,
    string Type,
    string Status,
    DateTimeOffset? CreatedAt,
    IReadOnlyList<string> Adds,
    IReadOnlyList<string> Drops);

public sealed record PlayerRecord(
    string PlayerId,
    string FullName,
    string? FirstName,
    string? LastName,
    string? Position,
    string? Team,
    string? InjuryStatus,
    string? PracticeParticipation,
    string? SearchRank,
    bool Active);

public sealed record LeagueState(
    string Sport,
    string Season,
    string Week,
    string SeasonType,
    string DisplayWeek);

public sealed record TrendingPlayer(
    string PlayerId,
    int Count,
    string Direction);

public sealed record LeagueSnapshot(
    LeagueSummary League,
    LeagueSettings Settings,
    IReadOnlyList<RosterRecord> Rosters,
    IReadOnlyList<MatchupRecord> Matchups,
    IReadOnlyList<TransactionRecord> Transactions,
    LeagueState State,
    IReadOnlyDictionary<string, PlayerRecord> Players,
    IReadOnlyList<TrendingPlayer> Trending);
