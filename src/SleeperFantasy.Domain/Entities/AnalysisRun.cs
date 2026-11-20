namespace SleeperFantasy.Domain.Entities;

public sealed class AnalysisRun
{
    public Guid Id { get; set; }
    public Guid ManagerConfigurationId { get; set; }
    public required string Status { get; set; }
    public int Attempt { get; set; }
    public string? CloudRunExecutionId { get; set; }
    public string? Stage { get; set; }
    public string? Provider { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? RetryOfRunId { get; set; }

    public ManagerConfiguration? ManagerConfiguration { get; set; }
    public AnalysisResult? Result { get; set; }
    public AnalysisRun? RetryOfRun { get; set; }
}
