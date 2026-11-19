using SleeperFantasy.Domain.Providers;

namespace SleeperFantasy.Domain.Analysis;

public sealed record EvidenceItem(
    string EvidenceId,
    string Provider,
    string EvidenceType,
    string SubjectKey,
    string Fact,
    DateTimeOffset ObservedAt,
    DateTimeOffset? FreshUntil,
    decimal? Confidence = null,
    string? IdentityKey = null,
    ProviderAvailability Availability = ProviderAvailability.Available)
{
    public bool IsStale(DateTimeOffset now) =>
        FreshUntil is not null && FreshUntil <= now;
}

public sealed record SourceFailure(
    string Provider,
    ProviderAvailability Availability,
    DateTimeOffset ObservedAt,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record SourceRefreshResult(
    LeagueSnapshot? League,
    IReadOnlyList<PlayerPerformanceRecord> Performance,
    IReadOnlyList<ScheduleRecord> Schedule,
    IReadOnlyList<PlayerUsageRecord> Usage,
    IReadOnlyList<InjuryReportRecord> Injuries,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<SourceFailure> Failures,
    DateTimeOffset RefreshedAt)
{
    public bool IsPartial => Failures.Count > 0;
    public bool IsUnavailable => League is null || Failures.Any(x =>
        x.Availability is ProviderAvailability.Unavailable
            or ProviderAvailability.RateLimited
            or ProviderAvailability.Invalid);
}

public sealed record PlayerRisk(
    string PlayerId,
    string PlayerName,
    string? Position,
    bool IsStarter,
    IReadOnlyList<string> RiskCodes,
    IReadOnlyList<string> UnavailableSignals,
    decimal? RecentPerformance,
    DateTimeOffset GeneratedAt);

public sealed record RosterRiskReport(
    string LeagueState,
    int CurrentWeek,
    IReadOnlyList<PlayerRisk> Players,
    IReadOnlyList<string> Warnings,
    bool IsAvailable,
    DateTimeOffset GeneratedAt);

public sealed record AvailablePlayer(
    string PlayerId,
    string FullName,
    string? Position,
    string? Team,
    DateTimeOffset SnapshotAt,
    IReadOnlyList<string> Eligibility,
    bool EvidenceComplete);

public sealed record AvailabilityPool(
    IReadOnlyList<AvailablePlayer> Players,
    DateTimeOffset SnapshotAt,
    bool IsComplete,
    IReadOnlyList<string> Warnings);

public sealed record WaiverRecommendation(
    string PlayerId,
    string PlayerName,
    string? Position,
    decimal Score,
    int Priority,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> Conflicts,
    decimal Confidence,
    bool IsStale);

public sealed record WaiverRanking(
    IReadOnlyList<WaiverRecommendation> Recommendations,
    bool IsComplete,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAt);

public sealed record DeterministicAnalysis(
    RosterRiskReport RosterRisk,
    AvailabilityPool Availability,
    WaiverRanking Waivers,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<SourceFailure> Failures,
    DateTimeOffset GeneratedAt);

public sealed record AnalysisReview(
    DeterministicAnalysis Deterministic,
    LanguageModelResponse? ContextualAnalysis,
    bool ContextualAnalysisAvailable,
    IReadOnlyList<string> Caveats,
    decimal Confidence,
    bool IsStale,
    DateTimeOffset GeneratedAt);
