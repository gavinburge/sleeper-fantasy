using SleeperFantasy.Domain.Analysis;

namespace SleeperFantasy.Application.Features.Analysis;

public sealed record AnalysisRunResponse(
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

public sealed record AnalysisLatestResponse(
    AnalysisRunResponse Run,
    AnalysisReview? Result);
