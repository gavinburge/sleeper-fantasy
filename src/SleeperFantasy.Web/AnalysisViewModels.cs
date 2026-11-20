using System.Text.Json;

namespace SleeperFantasy.Web;

public static class AnalysisViewModelParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static AnalysisLatestViewModel? ParseLatest(string json) =>
        JsonSerializer.Deserialize<AnalysisLatestViewModel>(json, JsonOptions);

    public static AnalysisRunViewModel? ParseRun(string json) =>
        JsonSerializer.Deserialize<AnalysisRunViewModel>(json, JsonOptions);
}

public sealed record AnalysisLatestViewModel(
    AnalysisRunViewModel Run,
    AnalysisReviewViewModel? Result);

public sealed record AnalysisRunViewModel(
    Guid Id,
    string Status,
    int Attempt,
    string? Stage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? LastHeartbeatAt,
    string? CloudRunExecutionId,
    string? Provider,
    string? ErrorCode,
    string? ErrorMessage,
    bool RetryAvailable,
    Guid? RetryOfRunId);

public sealed record AnalysisReviewViewModel(
    DeterministicAnalysisViewModel Deterministic,
    LanguageModelResponseViewModel? ContextualAnalysis,
    bool ContextualAnalysisAvailable,
    IReadOnlyList<string> Caveats,
    decimal Confidence,
    bool IsStale,
    DateTimeOffset GeneratedAt);

public sealed record DeterministicAnalysisViewModel(
    RosterRiskReportViewModel RosterRisk,
    AvailabilityPoolViewModel Availability,
    WaiverRankingViewModel Waivers,
    IReadOnlyList<EvidenceItemViewModel> Evidence,
    IReadOnlyList<SourceFailureViewModel> Failures,
    DateTimeOffset GeneratedAt);

public sealed record RosterRiskReportViewModel(
    string LeagueState,
    int CurrentWeek,
    IReadOnlyList<PlayerRiskViewModel> Players,
    IReadOnlyList<string> Warnings,
    bool IsAvailable,
    DateTimeOffset GeneratedAt);

public sealed record PlayerRiskViewModel(
    string PlayerId,
    string PlayerName,
    string? Position,
    bool IsStarter,
    IReadOnlyList<string> RiskCodes,
    IReadOnlyList<string> UnavailableSignals,
    decimal? RecentPerformance,
    DateTimeOffset GeneratedAt);

public sealed record AvailabilityPoolViewModel(
    IReadOnlyList<AvailablePlayerViewModel> Players,
    DateTimeOffset SnapshotAt,
    bool IsComplete,
    IReadOnlyList<string> Warnings);

public sealed record AvailablePlayerViewModel(
    string PlayerId,
    string FullName,
    string? Position,
    string? Team,
    DateTimeOffset SnapshotAt,
    IReadOnlyList<string> Eligibility,
    bool EvidenceComplete);

public sealed record WaiverRankingViewModel(
    IReadOnlyList<WaiverRecommendationViewModel> Recommendations,
    bool IsComplete,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAt);

public sealed record WaiverRecommendationViewModel(
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

public sealed record EvidenceItemViewModel(
    string EvidenceId,
    string Provider,
    string EvidenceType,
    string SubjectKey,
    string Fact,
    DateTimeOffset ObservedAt,
    DateTimeOffset? FreshUntil,
    decimal? Confidence,
    string? IdentityKey,
    int Availability);

public sealed record SourceFailureViewModel(
    string Provider,
    int Availability,
    DateTimeOffset ObservedAt,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record LanguageModelResponseViewModel(
    string Model,
    string? Summary,
    IReadOnlyList<LanguageModelFindingViewModel> Findings,
    IReadOnlyList<string> Conflicts,
    DateTimeOffset GeneratedAt);

public sealed record LanguageModelFindingViewModel(
    string SubjectKey,
    string Explanation,
    decimal Confidence,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> Caveats);
