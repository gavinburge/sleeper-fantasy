using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Providers;

namespace SleeperFantasy.Application.Features.Analysis;

public sealed class LanguageModelAnalysisService(ILanguageModelProvider provider)
{
    public async Task<AnalysisReview> MergeAsync(
        string analysisId,
        DeterministicAnalysis deterministic,
        CancellationToken cancellationToken)
    {
        var evidence = deterministic.Evidence
            .Select(x => new LanguageModelEvidence(
                x.EvidenceId,
                x.SubjectKey,
                x.Fact,
                x.Provider,
                x.ObservedAt,
                x.FreshUntil,
                x.Confidence))
            .ToArray();
        
        var request = new LanguageModelRequest(
            analysisId,
            "Explain only the supplied evidence. Do not invent player facts, projections, or availability.",
            "Summarize the deterministic roster risks and waiver recommendations, citing evidence IDs.",
            evidence);

        var result = await provider.AnalyzeAsync(request, cancellationToken);
        
        var caveats = new List<string>();
        
        if (!result.IsSuccessful || result.Value is null)
        {
            caveats.Add("Contextual analysis is unavailable; deterministic findings remain authoritative.");
            return new AnalysisReview(
                deterministic,
                null,
                false,
                caveats,
                CalculateConfidence(deterministic),
                deterministic.Failures.Count > 0,
                DateTimeOffset.UtcNow);
        }

        var validEvidence = evidence.Select(x => x.EvidenceId).ToHashSet(StringComparer.Ordinal);
        
        var validFindings = result.Value.Findings
            .Select(finding => finding with
            {
                EvidenceIds = finding.EvidenceIds.Where(validEvidence.Contains).Distinct().ToArray(),
                Confidence = Math.Clamp(finding.Confidence, 0m, 1m)
            })
            .ToArray();
        
        var response = result.Value with { Findings = validFindings };
        
        if (response.Findings.Any(x => x.EvidenceIds.Count == 0))
        {
            caveats.Add("Some model explanations did not cite valid evidence and were retained without factual authority.");
        }

        return new AnalysisReview(
            deterministic,
            response,
            true,
            caveats,
            CalculateConfidence(deterministic),
            deterministic.Failures.Count > 0 || deterministic.Waivers.Recommendations.Any(x => x.IsStale),
            DateTimeOffset.UtcNow);
    }

    private static decimal CalculateConfidence(DeterministicAnalysis deterministic)
    {
        var sourceConfidence = deterministic.Failures.Count == 0 ? 1m : 0.5m;
        
        var waiverConfidence = deterministic.Waivers.Recommendations.Count == 0
            ? 0.5m
            : deterministic.Waivers.Recommendations.Average(x => x.Confidence);
        
        return Math.Round((sourceConfidence + waiverConfidence) / 2m, 2);
    }
}
