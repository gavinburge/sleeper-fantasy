namespace SleeperFantasy.Domain.Entities;

public sealed class AnalysisEvidence
{
    public Guid Id { get; set; }
    public Guid AnalysisRunId { get; set; }
    public required string Provider { get; set; }
    public required string EvidenceType { get; set; }
    public required string SubjectKey { get; set; }
    public string? IdentityKey { get; set; }
    public required string PayloadJson { get; set; }
    public required string Availability { get; set; }
    public bool IsStale { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset? FreshUntil { get; set; }
}
