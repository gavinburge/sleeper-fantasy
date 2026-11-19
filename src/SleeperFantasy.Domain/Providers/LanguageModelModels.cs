namespace SleeperFantasy.Domain.Providers;

public sealed record LanguageModelEvidence(
    string EvidenceId,
    string SubjectKey,
    string Fact,
    string Source,
    DateTimeOffset ObservedAt,
    DateTimeOffset? FreshUntil,
    decimal? Confidence);

public sealed record LanguageModelRequest(
    string AnalysisId,
    string SystemInstruction,
    string UserPrompt,
    IReadOnlyList<LanguageModelEvidence> Evidence);

public sealed record LanguageModelFinding(
    string SubjectKey,
    string Explanation,
    decimal Confidence,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> Caveats);

public sealed record LanguageModelResponse(
    string Model,
    string? Summary,
    IReadOnlyList<LanguageModelFinding> Findings,
    IReadOnlyList<string> Conflicts,
    DateTimeOffset GeneratedAt);
