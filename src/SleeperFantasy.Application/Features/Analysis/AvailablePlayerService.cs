using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Providers;

namespace SleeperFantasy.Application.Features.Analysis;

public sealed class AvailablePlayerService
{
    public AvailabilityPool Derive(
        LeagueSnapshot snapshot,
        IReadOnlyList<SourceFailure> failures,
        DateTimeOffset now)
    {
        var warnings = new List<string>();
        
        var rosterSnapshotFailure = failures.Any(x =>
            x.Provider.Equals("Sleeper", StringComparison.OrdinalIgnoreCase)
            && x.ErrorCode?.Contains("ROSTER", StringComparison.OrdinalIgnoreCase) == true);
        
        var isComplete = !rosterSnapshotFailure && snapshot.Rosters.Count > 0;
        
        if (!isComplete)
        {
            warnings.Add("The league roster snapshot is incomplete; waiver availability cannot be verified.");
        }

        var rostered = snapshot.Rosters
            .SelectMany(x => x.PlayerIds)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        
        var supportedPositions = snapshot.Settings.RosterPositions.Keys
            .Where(x => !x.Equals("FLEX", StringComparison.OrdinalIgnoreCase)
                && !x.Equals("SUPER_FLEX", StringComparison.OrdinalIgnoreCase)
                && !x.Equals("BN", StringComparison.OrdinalIgnoreCase)
                && !x.Equals("IR", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        var flexPositions = snapshot.Settings.RosterPositions.Keys.Any(x =>
            x.Equals("FLEX", StringComparison.OrdinalIgnoreCase)
            || x.Equals("SUPER_FLEX", StringComparison.OrdinalIgnoreCase));

        var players = snapshot.Players.Values
            .Where(x => !rostered.Contains(x.PlayerId)
                && x.Active
                && !string.IsNullOrWhiteSpace(x.FullName)
                && !string.IsNullOrWhiteSpace(x.Position)
                && (supportedPositions.Contains(x.Position!)
                    || flexPositions && x.Position is "RB" or "WR" or "TE"))
            .Select(x => new AvailablePlayer(
                x.PlayerId,
                x.FullName,
                x.Position,
                x.Team,
                now,
                [x.Position!],
                isComplete && !string.IsNullOrWhiteSpace(x.Team)))
            .ToArray();

        return new AvailabilityPool(players, now, isComplete, warnings);
    }
}
