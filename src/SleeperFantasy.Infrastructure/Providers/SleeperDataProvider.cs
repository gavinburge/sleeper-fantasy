using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Infrastructure.Configuration;

namespace SleeperFantasy.Infrastructure.Providers;

public sealed class SleeperDataProvider(
    HttpClient httpClient,
    IOptions<CacheOptions> cacheOptions,
    ILogger<SleeperDataProvider> logger)
    : ProviderHttpClient(httpClient, cacheOptions, logger), ILeagueDataProvider
{
    public const string Name = "Sleeper";
    public string ProviderName => Name;

    public async Task<ProviderResult<SleeperUser>> GetUserAsync(
        string username,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<JsonElement>($"user/{Uri.EscapeDataString(username)}", cancellationToken);
        var value = response.Value;
        if (response.Availability != ProviderAvailability.Available || value.ValueKind != JsonValueKind.Object)
        {
            return Failure<SleeperUser>(response.Availability, response.ObservedAt);
        }

        var user = new SleeperUser(
            value.GetString("user_id") ?? string.Empty,
            value.GetString("username") ?? username,
            value.GetString("display_name"));
        return string.IsNullOrWhiteSpace(user.UserId)
            ? ProviderResult<SleeperUser>.Failure(
                ProviderAvailability.Invalid, Name, response.ObservedAt, "INVALID_USER", "Sleeper user response omitted user_id.")
            : ProviderResult<SleeperUser>.AvailableResult(user, Name, response.ObservedAt, response.FreshUntil);
    }

    public async Task<ProviderResult<IReadOnlyList<LeagueSummary>>> GetLeaguesAsync(
        string userId,
        string season,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<List<JsonElement>>(
            $"user/{Uri.EscapeDataString(userId)}/leagues/nfl/{Uri.EscapeDataString(season)}",
            cancellationToken);
        if (response.Value is null)
        {
            return Failure<IReadOnlyList<LeagueSummary>>(response.Availability, response.ObservedAt);
        }

        var leagues = response.Value
            .Select(ParseLeague)
            .Where(x => x is not null)
            .Cast<LeagueSummary>()
            .ToArray();
        return leagues.Length == 0
            ? ProviderResult<IReadOnlyList<LeagueSummary>>.EmptyResult(Name, response.ObservedAt, response.FreshUntil)
            : ProviderResult<IReadOnlyList<LeagueSummary>>.AvailableResult(leagues, Name, response.ObservedAt, response.FreshUntil);
    }

    public async Task<ProviderResult<LeagueSummary>> GetLeagueAsync(
        string leagueId,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<JsonElement>($"league/{Uri.EscapeDataString(leagueId)}", cancellationToken);
        var value = response.Value;
        if (response.Availability != ProviderAvailability.Available || value.ValueKind != JsonValueKind.Object)
        {
            return Failure<LeagueSummary>(response.Availability, response.ObservedAt);
        }

        var league = ParseLeague(value);
        return league is null
            ? ProviderResult<LeagueSummary>.Failure(ProviderAvailability.Invalid, Name, response.ObservedAt, "INVALID_LEAGUE", "Sleeper league response was invalid.")
            : ProviderResult<LeagueSummary>.AvailableResult(league, Name, response.ObservedAt, response.FreshUntil);
    }

    public async Task<ProviderResult<IReadOnlyList<RosterRecord>>> GetRostersAsync(
        string leagueId,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<List<JsonElement>>($"league/{Uri.EscapeDataString(leagueId)}/rosters", cancellationToken);
        if (response.Value is null)
        {
            return Failure<IReadOnlyList<RosterRecord>>(response.Availability, response.ObservedAt);
        }

        var rosters = response.Value.Select(ParseRoster).Where(x => x is not null).Cast<RosterRecord>().ToArray();
        return rosters.Length == 0
            ? ProviderResult<IReadOnlyList<RosterRecord>>.EmptyResult(Name, response.ObservedAt, response.FreshUntil)
            : ProviderResult<IReadOnlyList<RosterRecord>>.AvailableResult(rosters, Name, response.ObservedAt, response.FreshUntil);
    }

    public async Task<ProviderResult<LeagueSettings>> GetSettingsAsync(
        string leagueId,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<JsonElement>($"league/{Uri.EscapeDataString(leagueId)}", cancellationToken);
        var value = response.Value;
        if (response.Availability != ProviderAvailability.Available || value.ValueKind != JsonValueKind.Object)
        {
            return Failure<LeagueSettings>(response.Availability, response.ObservedAt);
        }

        var settings = ParseSettings(leagueId, value);
        return ProviderResult<LeagueSettings>.AvailableResult(settings, Name, response.ObservedAt, response.FreshUntil);
    }

    public async Task<ProviderResult<IReadOnlyList<MatchupRecord>>> GetMatchupsAsync(
        string leagueId,
        int week,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<List<JsonElement>>(
            $"league/{Uri.EscapeDataString(leagueId)}/matchups/{week}",
            cancellationToken);
        if (response.Value is null)
        {
            return Failure<IReadOnlyList<MatchupRecord>>(response.Availability, response.ObservedAt);
        }

        var matchups = response.Value.Select(x => new MatchupRecord(
                x.GetString("matchup_id") ?? string.Empty,
                x.GetString("roster_id") ?? string.Empty,
                week,
                x.TryGetProperty("points", out var points) && points.TryGetDecimal(out var score) ? score : null,
                x.GetString("matchup_period")))
            .Where(x => x.RosterId.Length > 0)
            .ToArray();
        return matchups.Length == 0
            ? ProviderResult<IReadOnlyList<MatchupRecord>>.EmptyResult(Name, response.ObservedAt, response.FreshUntil)
            : ProviderResult<IReadOnlyList<MatchupRecord>>.AvailableResult(matchups, Name, response.ObservedAt, response.FreshUntil);
    }

    public async Task<ProviderResult<IReadOnlyList<TransactionRecord>>> GetTransactionsAsync(
        string leagueId,
        int round,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<List<JsonElement>>(
            $"league/{Uri.EscapeDataString(leagueId)}/transactions/{round}",
            cancellationToken);
        if (response.Value is null)
        {
            return Failure<IReadOnlyList<TransactionRecord>>(response.Availability, response.ObservedAt);
        }

        var transactions = response.Value.Select(x => new TransactionRecord(
                x.GetString("transaction_id") ?? string.Empty,
                x.GetString("type") ?? "unknown",
                x.GetString("status") ?? "unknown",
                x.TryGetProperty("created", out var created) && created.TryGetInt64(out var milliseconds)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                    : null,
                ReadStringMap(x, "adds"),
                ReadStringMap(x, "drops")))
            .Where(x => x.TransactionId.Length > 0)
            .ToArray();
        return transactions.Length == 0
            ? ProviderResult<IReadOnlyList<TransactionRecord>>.EmptyResult(Name, response.ObservedAt, response.FreshUntil)
            : ProviderResult<IReadOnlyList<TransactionRecord>>.AvailableResult(transactions, Name, response.ObservedAt, response.FreshUntil);
    }

    public async Task<ProviderResult<IReadOnlyDictionary<string, PlayerRecord>>> GetPlayersAsync(
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<Dictionary<string, JsonElement>>("players/nfl", cancellationToken);
        if (response.Value is null)
        {
            return Failure<IReadOnlyDictionary<string, PlayerRecord>>(response.Availability, response.ObservedAt);
        }

        var players = response.Value
            .Select(pair => (pair.Key, Player: ParsePlayer(pair.Key, pair.Value)))
            .Where(x => x.Player is not null)
            .ToDictionary(x => x.Key, x => x.Player!);
        return players.Count == 0
            ? ProviderResult<IReadOnlyDictionary<string, PlayerRecord>>.EmptyResult(Name, response.ObservedAt, response.FreshUntil)
            : ProviderResult<IReadOnlyDictionary<string, PlayerRecord>>.AvailableResult(players, Name, response.ObservedAt, response.FreshUntil);
    }

    public async Task<ProviderResult<LeagueState>> GetStateAsync(CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<JsonElement>("state/nfl", cancellationToken);
        var value = response.Value;
        if (response.Availability != ProviderAvailability.Available || value.ValueKind != JsonValueKind.Object)
        {
            return Failure<LeagueState>(response.Availability, response.ObservedAt);
        }

        var state = new LeagueState(
            value.GetString("sport") ?? "nfl",
            value.GetString("season") ?? string.Empty,
            value.GetString("week") ?? string.Empty,
            value.GetString("season_type") ?? string.Empty,
            value.GetString("display_week"));
        return ProviderResult<LeagueState>.AvailableResult(state, Name, response.ObservedAt, response.FreshUntil);
    }

    public async Task<ProviderResult<IReadOnlyList<TrendingPlayer>>> GetTrendingPlayersAsync(
        string sport,
        string type,
        int lookbackHours,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<List<JsonElement>>(
            $"players/{Uri.EscapeDataString(sport)}/trending/{Uri.EscapeDataString(type)}?lookback_hours={lookbackHours}",
            cancellationToken);
        if (response.Value is null)
        {
            return Failure<IReadOnlyList<TrendingPlayer>>(response.Availability, response.ObservedAt);
        }

        var players = response.Value.Select(x => new TrendingPlayer(
                x.GetString("player_id") ?? string.Empty,
                x.TryGetProperty("count", out var count) && count.TryGetInt32(out var value) ? value : 0,
                type))
            .Where(x => x.PlayerId.Length > 0)
            .ToArray();
        return players.Length == 0
            ? ProviderResult<IReadOnlyList<TrendingPlayer>>.EmptyResult(Name, response.ObservedAt, response.FreshUntil)
            : ProviderResult<IReadOnlyList<TrendingPlayer>>.AvailableResult(players, Name, response.ObservedAt, response.FreshUntil);
    }

    private static LeagueSummary? ParseLeague(JsonElement value)
    {
        var id = value.GetString("league_id");
        return string.IsNullOrWhiteSpace(id)
            ? null
            : new LeagueSummary(
                id,
                value.GetString("name") ?? id,
                value.GetString("season") ?? string.Empty,
                value.GetString("status") ?? "unknown",
                value.GetString("sport") ?? "nfl",
                value.TryGetProperty("total_rosters", out var rosters) && rosters.TryGetInt32(out var total) ? total : 0,
                value.GetString("scoring_settings") is null ? null : "custom");
    }

    private static RosterRecord? ParseRoster(JsonElement value)
    {
        var id = value.GetString("roster_id");
        var owner = value.GetString("owner_id");
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(owner)
            ? null
            : new RosterRecord(
                id,
                owner,
                ReadStringMap(value, "players"),
                ReadStringMap(value, "starters"),
                value.TryGetProperty("settings", out var settings) ? ReadInt(settings, "wins") : null,
                value.TryGetProperty("settings", out settings) ? ReadInt(settings, "losses") : null,
                value.TryGetProperty("settings", out settings) ? ReadInt(settings, "ties") : null,
                value.TryGetProperty("settings", out settings) ? ReadDecimal(settings, "fpts") : null);
    }

    private static LeagueSettings ParseSettings(string leagueId, JsonElement value)
    {
        var values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (value.TryGetProperty("scoring_settings", out var scoring))
        {
            foreach (var item in scoring.EnumerateObject())
            {
                if (item.Value.TryGetDecimal(out var number))
                {
                    values[item.Name] = number;
                }
            }
        }

        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (value.TryGetProperty("roster_positions", out var rosterPositions))
        {
            foreach (var position in rosterPositions.EnumerateArray())
            {
                var name = position.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    positions[name] = positions.TryGetValue(name, out var count) ? count + 1 : 1;
                }
            }
        }

        return new LeagueSettings(
            leagueId,
            values,
            positions,
            value.TryGetProperty("settings", out var settings) ? ReadInt(settings, "leg") ?? 0 : 0,
            value.GetString("status") ?? "unknown");
    }

    private static PlayerRecord? ParsePlayer(string id, JsonElement value)
    {
        var name = value.GetString("full_name");
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new PlayerRecord(
                id,
                name,
                value.GetString("first_name"),
                value.GetString("last_name"),
                value.GetString("position"),
                value.GetString("team"),
                value.GetString("injury_status"),
                value.GetString("practice_participation"),
                value.GetString("search_rank"),
                value.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True);
    }

    private static IReadOnlyList<string> ReadStringMap(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();
    }

    private static int? ReadInt(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) && element.TryGetInt32(out var number) ? number : null;

    private static decimal? ReadDecimal(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) && element.TryGetDecimal(out var number) ? number : null;

    private static ProviderResult<T> Failure<T>(
        ProviderAvailability availability,
        DateTimeOffset observedAt) =>
        ProviderResult<T>.Failure(
            availability,
            Name,
            observedAt,
            availability.ToString().ToUpperInvariant(),
            $"The {Name} provider returned {availability.ToString().ToLowerInvariant()} data.");
}

internal static class JsonElementExtensions
{
    public static string? GetString(this JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
