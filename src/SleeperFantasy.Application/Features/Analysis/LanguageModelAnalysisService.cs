using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Providers;
using System.Text.Json;

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
        
        var stale = IsStale(deterministic);
        var deterministicSummary = JsonSerializer.Serialize(new
        {
            rosterRisks = deterministic.RosterRisk.Players.Select(x => new
            {
                x.PlayerId, x.PlayerName, x.IsStarter, x.RiskCodes, x.UnavailableSignals, x.RecentPerformance
            }).Take(50),
            waivers = deterministic.Waivers.Recommendations.Select(x => new
            {
                x.PlayerId, x.PlayerName, x.Score, x.Priority, x.Reasons, x.Conflicts, x.Confidence, x.IsStale
            }).Take(50),
            availablePlayers = deterministic.Availability.Players
                .Select(x => new { x.PlayerId, x.Position, x.Eligibility, x.EvidenceComplete })
                .Take(50),
            warnings = deterministic.RosterRisk.Warnings
                .Concat(deterministic.Waivers.Warnings)
                .Concat(deterministic.Availability.Warnings)
                .Take(50),
            failures = deterministic.Failures.Select(x => new { x.Provider, x.Availability, x.ErrorCode })
        });

        var request = new LanguageModelRequest(
            analysisId,
            "Explain only the supplied evidence and deterministic summary. Deterministic facts, scores, priorities, conflicts, and unavailable signals are authoritative. Do not change, override, or invent them.",
            "Summarize the deterministic results, citing evidence IDs where useful. You may explain the results and caveats only; you must not replace deterministic facts or rankings.",
            evidence,
            deterministicSummary);

        var result = await provider.AnalyzeAsync(request, cancellationToken);
        
        var caveats = new List<string>();
        
        if (!result.IsSuccessful || result.Value is null)
        {
            var failureDetail = string.IsNullOrWhiteSpace(result.ErrorCode)
                ? string.Empty
                : $" ({result.Provider}: {result.ErrorCode})";
            var failureMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? string.Empty
                : $" Details: {result.ErrorMessage}";
            caveats.Add(
                $"Contextual analysis is unavailable{failureDetail}.{failureMessage} " +
                "Deterministic findings remain authoritative.");
            return new AnalysisReview(
                deterministic,
                null,
                false,
                caveats,
                CalculateConfidence(deterministic),
                stale,
                DateTimeOffset.UtcNow);
        }

        var validEvidence = evidence.Select(x => x.EvidenceId).ToHashSet(StringComparer.Ordinal);
        
        var validFindings = result.Value.Findings
            .Select(finding => finding with
            {
                EvidenceIds = finding.EvidenceIds.Where(validEvidence.Contains).Distinct().ToArray(),
                Confidence = Math.Clamp(finding.Confidence, 0m, 1m)
            })
            .Where(finding => finding.EvidenceIds.Count > 0)
            .ToArray();
        
        var response = result.Value with { Findings = validFindings };
        
        return new AnalysisReview(
            deterministic,
            response,
            true,
            caveats,
            CalculateConfidence(deterministic),
            stale,
            DateTimeOffset.UtcNow);
    }

    private static bool IsStale(DeterministicAnalysis deterministic) =>
        deterministic.Failures.Count > 0
        || !deterministic.RosterRisk.IsAvailable
        || !deterministic.Availability.IsComplete
        || deterministic.Waivers.Recommendations.Any(x => x.IsStale)
        || deterministic.Evidence.Any(x => x.IsStale(DateTimeOffset.UtcNow));

    private static decimal CalculateConfidence(DeterministicAnalysis deterministic)
    {
        var sourceConfidence = deterministic.Failures.Count == 0 ? 1m : 0.5m;
        
        var waiverConfidence = deterministic.Waivers.Recommendations.Count == 0
            ? 0.5m
            : deterministic.Waivers.Recommendations.Average(x => x.Confidence);
        
        return Math.Round((sourceConfidence + waiverConfidence) / 2m, 2);
    }
}
