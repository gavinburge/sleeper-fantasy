using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Providers;

namespace SleeperFantasy.Application.Features.Analysis;

public sealed class RosterRiskAnalyzer
{
    public RosterRiskReport Analyze(
        LeagueSnapshot snapshot,
        IReadOnlyList<PlayerPerformanceRecord> performance,
        IReadOnlyList<ScheduleRecord> schedule,
        IReadOnlyList<InjuryReportRecord> injuries,
        string? rosterId,
        DateTimeOffset now)
    {
        var state = snapshot.State.SeasonType;
        if (!IsInSeason(snapshot.League.Status, state))
        {
            return new RosterRiskReport(
                state,
                ParseWeek(snapshot.State.Week),
                [],
                [$"Weekly analysis is unavailable while the league is {snapshot.League.Status} ({state})."],
                false,
                now);
        }

        var currentWeek = ParseWeek(snapshot.State.Week);
        var roster = snapshot.Rosters.FirstOrDefault(x => x.RosterId == rosterId) ?? snapshot.Rosters.FirstOrDefault();
        var warnings = new List<string>();
        
        if (roster is null)
        {
            return new RosterRiskReport(state, currentWeek, [], ["The selected roster was not found."], false, now);
        }

        var injuryByName = injuries
            .GroupBy(x => Normalize(x.PlayerName))
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var currentWeekSchedule = schedule.Where(x => x.Week == currentWeek).ToArray();
        
        var players = new List<PlayerRisk>();
        
        foreach (var playerId in roster.PlayerIds.Distinct(StringComparer.Ordinal))
        {
            if (!snapshot.Players.TryGetValue(playerId, out var player)
                || string.IsNullOrWhiteSpace(player.FullName))
            {
                warnings.Add($"Player {playerId} is missing from the player snapshot.");
                continue;
            }

            var risks = new List<string>();
            var unavailable = new List<string>();
            
            var isStarter = roster.StarterIds.Contains(playerId, StringComparer.Ordinal);
            var injury = injuryByName.GetValueOrDefault(Normalize(player.FullName));
            
            if (injury is not null)
            {
                if (injury.Status is GameStatus.Out or GameStatus.InjuredReserve)
                {
                    risks.Add("injury");
                }
                else if (injury.Status is GameStatus.Questionable or GameStatus.Doubtful)
                {
                    risks.Add("injury-risk");
                }
                if (injury.PracticeStatus is null)
                {
                    unavailable.Add("practice-status");
                }
            }
            else if (player.InjuryStatus is null)
            {
                unavailable.Add("injury-status");
            }

            if (player.PracticeParticipation is null)
            {
                unavailable.Add("practice-status");
            }

            if (player.Team is null)
            {
                unavailable.Add("team");
            }
            else if (currentWeekSchedule.Length > 0
                     && currentWeekSchedule.All(x => !string.Equals(x.HomeTeam, player.Team, StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(x.AwayTeam, player.Team, StringComparison.OrdinalIgnoreCase)))
            {
                risks.Add("bye");
            }

            var recent = performance
                .Where(x => x.PlayerId == playerId && x.Week < currentWeek)
                .OrderByDescending(x => x.Week)
                .Select(x => x.Statistics.TryGetValue("fantasy_points", out var points)
                    ? points
                    : x.Statistics.TryGetValue("fantasy_points_ppr", out var ppr) ? ppr : (decimal?)null)
                .FirstOrDefault();
            
            if (recent is null)
            {
                unavailable.Add("recent-performance");
            }

            players.Add(new PlayerRisk(
                playerId,
                player.FullName,
                player.Position,
                isStarter,
                risks,
                unavailable.Distinct(StringComparer.Ordinal).ToArray(),
                recent,
                now));
        }

        return new RosterRiskReport(state, currentWeek, players, warnings, true, now);
    }

    private static bool IsInSeason(string status, string seasonType) =>
        status.Equals("in_season", StringComparison.OrdinalIgnoreCase)
        && (seasonType.Equals("regular", StringComparison.OrdinalIgnoreCase)
            || seasonType.Equals("post", StringComparison.OrdinalIgnoreCase)
            || seasonType.Equals("postseason", StringComparison.OrdinalIgnoreCase));

    private static int ParseWeek(string value) =>
        int.TryParse(value, out var week) ? week : 0;

    private static string Normalize(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
