using SleeperFantasy.Domain.Providers;

namespace SleeperFantasy.Application.Providers;

public sealed record IdentityMatch(
    string SleeperPlayerId,
    string? ProviderPlayerId,
    decimal Confidence,
    bool IsAmbiguous,
    string? Warning);

public interface IPlayerIdentityResolver
{
    IdentityMatch Resolve(
        PlayerRecord sleeperPlayer,
        IReadOnlyCollection<ExternalPlayerRecord> candidates,
        string provider);
}

public sealed class PlayerIdentityResolver : IPlayerIdentityResolver
{
    public IdentityMatch Resolve(
        PlayerRecord sleeperPlayer,
        IReadOnlyCollection<ExternalPlayerRecord> candidates,
        string provider)
    {
        var exactId = candidates.FirstOrDefault(x =>
            string.Equals(x.GsisId, sleeperPlayer.PlayerId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.ProviderPlayerId, sleeperPlayer.PlayerId, StringComparison.OrdinalIgnoreCase));
        if (exactId is not null)
        {
            return new IdentityMatch(sleeperPlayer.PlayerId, exactId.ProviderPlayerId, 1m, false, null);
        }

        var scored = candidates
            .Select(candidate => (Candidate: candidate, Score: Score(sleeperPlayer, candidate)))
            .Where(x => x.Score >= 0.85m)
            .OrderByDescending(x => x.Score)
            .ToArray();
        if (scored.Length == 0)
        {
            return new IdentityMatch(
                sleeperPlayer.PlayerId,
                null,
                0m,
                false,
                $"No conservative {provider} identity match was found for {sleeperPlayer.FullName}.");
        }

        if (scored.Length > 1 && scored[0].Score - scored[1].Score < 0.1m)
        {
            return new IdentityMatch(
                sleeperPlayer.PlayerId,
                null,
                scored[0].Score,
                true,
                $"Multiple {provider} identities match {sleeperPlayer.FullName}; no identity was selected.");
        }

        return new IdentityMatch(sleeperPlayer.PlayerId, scored[0].Candidate.ProviderPlayerId, scored[0].Score, false, null);
    }

    private static decimal Score(PlayerRecord sleeper, ExternalPlayerRecord external)
    {
        var score = 0m;
        if (Normalize(sleeper.FullName) == Normalize(external.FullName))
        {
            score += 0.75m;
        }
        else if (NamePartsOverlap(sleeper.FullName, external.FullName))
        {
            score += 0.45m;
        }

        if (!string.IsNullOrWhiteSpace(sleeper.Team)
            && string.Equals(sleeper.Team, external.Team, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.15m;
        }

        if (!string.IsNullOrWhiteSpace(sleeper.Position)
            && string.Equals(sleeper.Position, external.Position, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.15m;
        }

        return Math.Min(score, 1m);
    }

    private static bool NamePartsOverlap(string first, string? second)
    {
        if (string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        var firstParts = Normalize(first).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var secondParts = Normalize(second).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return firstParts.Intersect(secondParts, StringComparer.OrdinalIgnoreCase).Count() >= 2;
    }

    private static string Normalize(string? value) =>
        string.Join(
            ' ',
            new string(
                    (value ?? string.Empty)
                        .ToLowerInvariant()
                        .Where(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
                        .ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
