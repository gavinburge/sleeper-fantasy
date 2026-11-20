using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Providers;

namespace SleeperFantasy.Application.Features.Analysis;

public sealed class WaiverRankingService
{
    private const int MaxRecommendationsPerPosition = 10;

    public WaiverRanking Rank(
        LeagueSnapshot snapshot,
        RosterRiskReport rosterRisk,
        AvailabilityPool availability,
        IReadOnlyList<PlayerPerformanceRecord> performance,
        IReadOnlyList<PlayerUsageRecord> usage,
        IReadOnlyList<InjuryReportRecord> injuries,
        DateTimeOffset now,
        IReadOnlyList<EvidenceItem>? evidence = null)
    {
        evidence ??= [];
        var needs = rosterRisk.Players
            .Where(x => x.IsStarter && x.RiskCodes.Count > 0)
            .Select(x => snapshot.Players.GetValueOrDefault(x.PlayerId)?.Position)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        
        var injuryNames = injuries
            .Where(x => x.Status is GameStatus.Out or GameStatus.InjuredReserve)
            .Select(x => Normalize(x.PlayerName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        var recommendations = new List<WaiverRecommendation>();

        foreach (var player in availability.Players)
        {
            var evidenceIds = new List<string>();
            var reasons = new List<string>();
            var conflicts = new List<string>();
            var score = 0m;
            
            if (player.Position is not null && needs.TryGetValue(player.Position, out var need))
            {
                score += need * 40m;
                reasons.Add($"Fills a {player.Position} roster need.");
                AddEvidence(evidenceIds, evidence, "rosters");
                AddEvidence(evidenceIds, evidence, "league-settings");
            }

            var playerPerformance = performance
                .Where(x => x.PlayerId == player.PlayerId)
                .SelectMany(x => x.Statistics)
                .Where(x => x.Key.Contains("fantasy", StringComparison.OrdinalIgnoreCase))
                .Select(x => (decimal?)x.Value)
                .DefaultIfEmpty()
                .Max();
            
            if (playerPerformance > 0)
            {
                score += Math.Min(playerPerformance.Value, 30m);
                reasons.Add("Has recent fantasy performance evidence.");
                AddEvidence(evidenceIds, evidence, "performance");
            }

            if (snapshot.Settings.Values.TryGetValue("rec", out var receptionValue)
                || snapshot.Settings.Values.TryGetValue("rec_pts", out receptionValue))
            {
                var receptions = performance
                    .Where(x => x.PlayerId == player.PlayerId)
                    .SelectMany(x => x.Statistics)
                    .Where(x => x.Key.Equals("receptions", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Value)
                    .DefaultIfEmpty()
                    .Max();
                if (receptions > 0)
                {
                    score += receptions * receptionValue;
                    reasons.Add("Scoring settings reward the player's reception volume.");
                    AddEvidence(evidenceIds, evidence, "league-settings");
                    AddEvidence(evidenceIds, evidence, "performance");
                }
            }

            var usageRecord = usage.FirstOrDefault(x => x.ProviderPlayerId == player.PlayerId);
            if (usageRecord is not null)
            {
                var opportunity = usageRecord.Usage
                    .Where(x => x.Key.Contains("snap", StringComparison.OrdinalIgnoreCase)
                        || x.Key.Contains("route", StringComparison.OrdinalIgnoreCase)
                        || x.Key.Contains("touch", StringComparison.OrdinalIgnoreCase)
                        || x.Key.Contains("target", StringComparison.OrdinalIgnoreCase)
                        || x.Key.Equals("carries", StringComparison.OrdinalIgnoreCase)
                        || x.Key.Equals("receptions", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Value)
                    .DefaultIfEmpty()
                    .Max();
                score += Math.Min(opportunity, 20m);
                reasons.Add("Has role or usage evidence.");
                AddEvidence(evidenceIds, evidence, "usage");
            }

            if (injuryNames.Contains(Normalize(player.FullName)))
            {
                reasons.Add("Has an injury-related opportunity signal.");
                score += 15m;
                AddEvidence(evidenceIds, evidence, "injury");
            }

            var evidenceComplete = player.EvidenceComplete
                && playerPerformance > 0
                && usageRecord is not null;
            
            if (!player.EvidenceComplete)
            {
                reasons.Add("Availability evidence is incomplete.");
            }
            
            if (playerPerformance <= 0)
            {
                reasons.Add("Recent performance is unavailable.");
            }
            
            if (usageRecord is null)
            {
                reasons.Add("Role or usage evidence is unavailable.");
            }

            if (usageRecord is not null && playerPerformance <= 0)
            {
                conflicts.Add("Role evidence is present but recent performance evidence is unavailable.");
            }

            var confidence = evidenceComplete ? 0.85m : 0.5m;
            
            recommendations.Add(new WaiverRecommendation(
                player.PlayerId,
                player.FullName,
                player.Position,
                score,
                0,
                reasons,
                evidenceIds.Distinct(StringComparer.Ordinal).ToArray(),
                conflicts,
                confidence,
                !availability.IsComplete));
        }

        var ordered = recommendations
            .Where(x => x.Score > 0m)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.PlayerName, StringComparer.Ordinal)
            .GroupBy(x => x.Position ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .SelectMany(x => x.Take(MaxRecommendationsPerPosition))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.PlayerName, StringComparer.Ordinal)
            .Select((x, index) => x with { Priority = index + 1 })
            .ToArray();
        
        return new WaiverRanking(
            ordered,
            availability.IsComplete,
            availability.Warnings,
            now);
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static void AddEvidence(
        ICollection<string> evidenceIds,
        IReadOnlyList<EvidenceItem> evidence,
        string evidenceType)
    {
        foreach (var evidenceId in evidence
                     .Where(x => x.EvidenceType.Equals(evidenceType, StringComparison.OrdinalIgnoreCase))
                     .Select(x => x.EvidenceId))
        {
            evidenceIds.Add(evidenceId);
        }
    }
}
