namespace SleeperFantasy.Domain.Analysis;

public static class AnalysisRunStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Retryable = "retryable";
    public const string Stale = "stale";

    public static bool IsActive(string status) =>
        status is Queued or Running;

    public static bool IsTerminal(string status) =>
        status is Completed or Failed or Retryable or Stale;
}
