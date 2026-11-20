using SleeperFantasy.Domain.Analysis;

namespace SleeperFantasy.Application.Features.Analysis;

public static class AnalysisReviewLimiter
{
    private const int MaxRecommendationsPerPosition = 3;
    private const int MaxRecommendations = 20;
    private const int MaxEvidencePerReference = 10;
    private const int MaxTransportEvidenceCharacters = 4_096;

    public static AnalysisReview Limit(AnalysisReview review)
    {
        var recommendations = review.Deterministic.Waivers.Recommendations
            .GroupBy(x => x.Position ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .SelectMany(x => x.Take(MaxRecommendationsPerPosition))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.PlayerName, StringComparer.Ordinal)
            .Take(MaxRecommendations)
            .Select(x => x with
            {
                EvidenceIds = x.EvidenceIds
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaxEvidencePerReference)
                    .ToArray()
            })
            .Select((x, index) => x with { Priority = index + 1 })
            .ToArray();
        var recommendedPlayerIds = recommendations
            .Select(x => x.PlayerId)
            .ToHashSet(StringComparer.Ordinal);
        var contextualAnalysis = review.ContextualAnalysis is null
            ? null
            : review.ContextualAnalysis with
            {
                Findings = review.ContextualAnalysis.Findings
                    .Select(x => x with
                    {
                        EvidenceIds = x.EvidenceIds
                            .Distinct(StringComparer.Ordinal)
                            .Take(MaxEvidencePerReference)
                            .ToArray()
                    })
                    .ToArray()
            };
        var referencedEvidenceIds = recommendations
            .SelectMany(x => x.EvidenceIds)
            .Concat(contextualAnalysis?.Findings.SelectMany(x => x.EvidenceIds) ?? [])
            .ToHashSet(StringComparer.Ordinal);
        var deterministic = review.Deterministic with
        {
            Availability = review.Deterministic.Availability with
            {
                Players = review.Deterministic.Availability.Players
                    .Where(x => recommendedPlayerIds.Contains(x.PlayerId))
                    .ToArray()
            },
            Waivers = review.Deterministic.Waivers with
            {
                Recommendations = recommendations
            },
            Evidence = review.Deterministic.Evidence
                .Where(x => referencedEvidenceIds.Contains(x.EvidenceId))
                .ToArray()
        };

        return review with
        {
            Deterministic = deterministic,
            ContextualAnalysis = contextualAnalysis
        };
    }

    public static AnalysisReview LimitForTransport(AnalysisReview review)
    {
        var limited = Limit(review);
        var evidence = limited.Deterministic.Evidence
            .Select(item => item.Fact.Length <= MaxTransportEvidenceCharacters
                ? item
                : item with
                {
                    Fact = $"Evidence payload omitted from the response because it exceeded {MaxTransportEvidenceCharacters} characters."
                })
            .ToArray();

        return limited with
        {
            Deterministic = limited.Deterministic with
            {
                Evidence = evidence
            }
        };
    }
}
