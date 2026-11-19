namespace SleeperFantasy.Domain.Entities;

public sealed class AnalysisResult
{
    public Guid Id { get; set; }
    public Guid AnalysisRunId { get; set; }
    public required string ResultJson { get; set; }
    public required string ResultVersion { get; set; }
    public decimal Confidence { get; set; }
    public bool IsStale { get; set; }
    public required string SourceManifestJson { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }

    public AnalysisRun? AnalysisRun { get; set; }
}
