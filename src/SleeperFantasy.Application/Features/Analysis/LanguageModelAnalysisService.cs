using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Providers;
using System.Text.Json;

namespace SleeperFantasy.Application.Features.Analysis;

public sealed class LanguageModelAnalysisService(ILanguageModelProvider provider)
{
    private const int MaxEvidencePerCandidate = 10;
    private const int MaxEvidenceCharacters = 4_096;
    private const int MaxShortlistCandidates = 20;

    public async Task<AnalysisReview> MergeAsync(
        string analysisId,
        DeterministicAnalysis deterministic,
        CancellationToken cancellationToken)
    {
        var shortlist = deterministic.Waivers.Recommendations;
        var shortlistIds = shortlist
            .Select(x => x.PlayerId)
            .ToHashSet(StringComparer.Ordinal);
        var shortlistEvidenceIds = shortlist
            .SelectMany(x => x.EvidenceIds.Take(MaxEvidencePerCandidate))
            .ToHashSet(StringComparer.Ordinal);
        var evidence = (shortlistEvidenceIds.Count == 0
                ? deterministic.Evidence
                : deterministic.Evidence.Where(x => shortlistEvidenceIds.Contains(x.EvidenceId)))
            .Take(100)
            .Select(x => new LanguageModelEvidence(
                x.EvidenceId,
                x.SubjectKey,
                CompactEvidence(x.Fact),
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
                x.PlayerId, x.PlayerName, x.Position, x.Score, x.Priority, x.Reasons, x.Conflicts,
                x.Confidence, x.IsStale, evidenceIds = x.EvidenceIds.Take(MaxEvidencePerCandidate)
            }).Take(MaxShortlistCandidates),
            availablePlayers = deterministic.Availability.Players
                .Where(x => shortlistIds.Contains(x.PlayerId))
                .Select(x => new { x.PlayerId, x.FullName, x.Position, x.Team, x.Eligibility, x.EvidenceComplete }),
            rosterNeeds = deterministic.RosterRisk.Players
                .Where(x => x.IsStarter && x.RiskCodes.Count > 0)
                .GroupBy(x => x.Position ?? "UNKNOWN", StringComparer.OrdinalIgnoreCase)
                .Select(x => new { position = x.Key, priority = x.Count() }),
            currentRoster = deterministic.RosterRisk.Players.Select(x => new
            {
                x.PlayerId, x.PlayerName, x.Position, x.IsStarter, x.RiskCodes, x.UnavailableSignals
            }),
            warnings = deterministic.RosterRisk.Warnings
                .Concat(deterministic.Waivers.Warnings)
                .Concat(deterministic.Availability.Warnings)
                .Take(50),
            failures = deterministic.Failures.Select(x => new { x.Provider, x.Availability, x.ErrorCode })
        });

        var request = new LanguageModelRequest(
            analysisId,
            "Use only the supplied evidence and deterministic summary. Deterministic facts, scores, conflicts, and unavailable signals are authoritative. Do not invent players, evidence, scores, teams, or roster needs.",
            "Choose up to three player IDs per position from the supplied top-ten shortlist, prioritizing the roster's greatest needs while considering player score, team, and evidence. Return those IDs in selectedPlayerIds. Summarize the recommendations and cite evidence IDs where useful.",
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
        
        var validCandidateIds = shortlistIds;
        var selectedIds = (result.Value.SelectedPlayerIds ?? [])
            .Where(validCandidateIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var selectedRecommendations = shortlist
            .Where(x => selectedIds.Contains(x.PlayerId))
            .GroupBy(x => x.Position ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .SelectMany(x => x.Take(3))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.PlayerName, StringComparer.Ordinal)
            .Select((x, index) => x with { Priority = index + 1 })
            .ToArray();
        if (selectedRecommendations.Length == 0)
        {
            selectedRecommendations = shortlist
                .GroupBy(x => x.Position ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .SelectMany(x => x.Take(3))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.PlayerName, StringComparer.Ordinal)
                .Select((x, index) => x with { Priority = index + 1 })
                .ToArray();
        }

        var selectedRecommendationIds = selectedRecommendations
            .Select(x => x.PlayerId)
            .ToHashSet(StringComparer.Ordinal);
        var selectedDeterministic = shortlist.Count == 0
            ? deterministic
            : deterministic with
            {
                Availability = deterministic.Availability with
                {
                    Players = deterministic.Availability.Players
                        .Where(x => selectedRecommendationIds.Contains(x.PlayerId))
                        .ToArray()
                },
                Waivers = deterministic.Waivers with
                {
                    Recommendations = selectedRecommendations
                }
            };
        var response = result.Value with
        {
            Findings = validFindings,
            SelectedPlayerIds = selectedRecommendations.Select(x => x.PlayerId).ToArray()
        };
        
        return new AnalysisReview(
            selectedDeterministic,
            response,
            true,
            caveats,
            CalculateConfidence(deterministic),
            stale,
            DateTimeOffset.UtcNow);
    }

    private static string CompactEvidence(string fact) =>
        fact.Length <= MaxEvidenceCharacters
            ? fact
            : $"{fact[..MaxEvidenceCharacters]} [truncated]";

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
