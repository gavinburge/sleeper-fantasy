using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Infrastructure.Configuration;

namespace SleeperFantasy.Infrastructure.Providers;

public sealed class NflverseDataProvider(
    HttpClient httpClient,
    IOptions<CacheOptions> cacheOptions,
    ILogger<NflverseDataProvider> logger)
    : ProviderHttpClient(httpClient, cacheOptions, logger), IPerformanceDataProvider
{
    public const string Name = "nflverse";
    public string ProviderName => Name;

    public Task<ProviderResult<IReadOnlyList<ScheduleRecord>>> GetScheduleAsync(
        string season,
        CancellationToken cancellationToken) =>
        GetCsvAsync(
            "schedules/games.csv",
            rows => ParseSchedule(rows, season),
            cancellationToken);

    public Task<ProviderResult<IReadOnlyList<PlayerPerformanceRecord>>> GetWeeklyPlayerStatsAsync(
        string season,
        int week,
        CancellationToken cancellationToken) =>
        GetCsvAsync(
            $"stats_player/stats_player_week_{season}.csv",
            rows => ParsePlayerStats(rows, season, week),
            cancellationToken);

    public Task<ProviderResult<IReadOnlyList<ExternalPlayerRecord>>> GetRostersAsync(
        string season,
        CancellationToken cancellationToken) =>
        GetCsvAsync(
            $"weekly_rosters/roster_weekly_{season}.csv",
            ParseRoster,
            cancellationToken);

    public Task<ProviderResult<IReadOnlyList<PlayerUsageRecord>>> GetUsageAsync(
        string season,
        CancellationToken cancellationToken) =>
        GetCsvAsync(
            $"stats_player/stats_player_week_{season}.csv",
            rows => ParseUsage(rows, season),
            cancellationToken);

    private async Task<ProviderResult<IReadOnlyList<T>>> GetCsvAsync<T>(
        string path,
        Func<IReadOnlyList<IReadOnlyDictionary<string, string>>, IReadOnlyList<T>> parser,
        CancellationToken cancellationToken)
    {
        var response = await GetTextAsync(path, cancellationToken);
        if (response.Availability == ProviderAvailability.Empty)
        {
            logger.LogWarning("Provider {Provider} resource {Resource} returned an empty payload.", Name, path);
            return ProviderResult<IReadOnlyList<T>>.EmptyResult(
                Name,
                response.ObservedAt,
                response.FreshUntil);
        }

        if (response.Availability != ProviderAvailability.Available || response.Value is null)
        {
            logger.LogWarning("Provider {Provider} resource {Resource} returned {Availability} data.", Name, path, response.Availability);
            return ProviderResult<IReadOnlyList<T>>.Failure(
                response.Availability,
                Name,
                response.ObservedAt,
                response.Availability.ToString().ToUpperInvariant(),
                $"The nflverse provider returned {response.Availability.ToString().ToLowerInvariant()} data.");
        }

        try
        {
            var records = parser(ParseCsv(response.Value));
            logger.LogInformation("Provider {Provider} resource {Resource} produced {RecordCount} records.", Name, path, records.Count);
            return records.Count == 0
                ? ProviderResult<IReadOnlyList<T>>.EmptyResult(Name, response.ObservedAt, response.FreshUntil)
                : ProviderResult<IReadOnlyList<T>>.AvailableResult(records, Name, response.ObservedAt, response.FreshUntil);
        }
        catch (FormatException exception)
        {
            logger.LogWarning(exception, "Provider {Provider} resource {Resource} contained malformed CSV.", Name, path);
            return ProviderResult<IReadOnlyList<T>>.Failure(
                ProviderAvailability.Invalid,
                Name,
                response.ObservedAt,
                "MALFORMED_CSV",
                exception.Message);
        }
    }

    private static IReadOnlyList<ScheduleRecord> ParseSchedule(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        string season) =>
        rows.Where(row => string.Equals(Get(row, "season"), season, StringComparison.OrdinalIgnoreCase))
            .Select(row => new ScheduleRecord(
                Get(row, "game_id") ?? string.Empty,
                Get(row, "season") ?? string.Empty,
                ParseInt(Get(row, "week")),
                Get(row, "gameday") ?? Get(row, "game_date"),
                Get(row, "home_team"),
                Get(row, "away_team"),
                Get(row, "game_type")))
            .Where(x => x.GameId.Length > 0)
            .ToArray();

    private static IReadOnlyList<PlayerPerformanceRecord> ParsePlayerStats(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        string season,
        int week) =>
        rows.Where(row => ParseInt(Get(row, "week")) == week)
            .Select(row => new PlayerPerformanceRecord(
                Get(row, "player_id") ?? Get(row, "gsis_id") ?? string.Empty,
                Get(row, "recent_team") ?? Get(row, "team"),
                Get(row, "position"),
                season,
                week,
                row.Where(x => x.Value is not null && decimal.TryParse(x.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                    .ToDictionary(
                        x => x.Key,
                        x => decimal.Parse(x.Value, NumberStyles.Any, CultureInfo.InvariantCulture)),
                DateTimeOffset.UtcNow))
            .Where(x => x.PlayerId.Length > 0)
            .ToArray();

    private static IReadOnlyList<ExternalPlayerRecord> ParseRoster(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows) =>
        rows.Select(row => new
            {
                Identity = Get(row, "gsis_id") ?? Get(row, "player_id") ?? string.Empty,
                Row = row
            })
            .Where(x => x.Identity.Length > 0)
            .GroupBy(x => x.Identity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => ParseInt(Get(x.Row, "week")))
                .Select(x => x.Row)
                .First())
            .Select(row => new ExternalPlayerRecord(
                Get(row, "gsis_id") ?? Get(row, "player_id") ?? string.Empty,
                Get(row, "full_name") ?? Get(row, "display_name"),
                Get(row, "team") ?? Get(row, "team_abbr"),
                Get(row, "position"),
                Get(row, "gsis_id"),
                DateTimeOffset.UtcNow))
            .ToArray();

    private static IReadOnlyList<PlayerUsageRecord> ParseUsage(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        string season) =>
        rows.Where(row => string.Equals(Get(row, "season"), season, StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => Get(row, "player_id") ?? Get(row, "gsis_id") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => ParseInt(Get(row, "week")))
                .First())
            .Select(row => new PlayerUsageRecord(
                Get(row, "player_id") ?? Get(row, "gsis_id") ?? string.Empty,
                Get(row, "recent_team") ?? Get(row, "team"),
                Get(row, "position"),
                BuildUsage(row)
                    .ToDictionary(
                        x => x.Key,
                        x => x.Value),
                DateTimeOffset.UtcNow))
            .Where(x => x.ProviderPlayerId.Length > 0)
            .ToArray();

    private static IReadOnlyDictionary<string, decimal> BuildUsage(
        IReadOnlyDictionary<string, string> row)
    {
        var usage = row
            .Where(x => x.Value is not null
                && decimal.TryParse(x.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            .ToDictionary(
                x => x.Key,
                x => decimal.Parse(x.Value, NumberStyles.Any, CultureInfo.InvariantCulture),
                StringComparer.OrdinalIgnoreCase);

        var carries = usage.GetValueOrDefault("carries");
        var receptions = usage.GetValueOrDefault("receptions");
        if (carries > 0 || receptions > 0)
        {
            usage["touches"] = carries + receptions;
        }

        return usage;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ParseCsv(string csv)
    {
        var lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return [];
        }

        var headers = SplitCsvLine(lines[0]);
        return lines.Skip(1)
            .Select(line => SplitCsvLine(line))
            .Where(values => values.Count > 0)
            .Select(values => headers
                .Select((header, index) => new
                {
                    Header = header,
                    Value = index < values.Count ? values[index] : string.Empty
                })
                .ToDictionary(x => x.Header, x => x.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    private static IReadOnlyList<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        if (quoted)
        {
            throw new FormatException("nflverse CSV contained an unterminated quoted field.");
        }

        values.Add(current.ToString());
        return values;
    }

    private static string? Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

}
