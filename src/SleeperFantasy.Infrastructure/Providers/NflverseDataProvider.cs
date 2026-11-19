using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Infrastructure.Configuration;

namespace SleeperFantasy.Infrastructure.Providers;

public sealed class NflverseDataProvider(
    HttpClient httpClient,
    IOptions<CacheOptions> cacheOptions,
    ILogger<NflverseDataProvider> logger,
    IProviderResponseCache? sharedCache = null)
    : ProviderHttpClient(httpClient, cacheOptions, logger, sharedCache), IPerformanceDataProvider
{
    public const string Name = "nflverse";
    public string ProviderName => Name;
    protected override TimeSpan CacheTtl => TimeSpan.FromHours(CacheSettings.EffectiveNflverseTtlHours);

    public Task<ProviderResult<IReadOnlyList<ScheduleRecord>>> GetScheduleAsync(
        string season,
        CancellationToken cancellationToken) =>
        GetCsvAsync(
            "schedules/games.csv",
            (rows, _) => ParseSchedule(rows, season),
            cancellationToken);

    public Task<ProviderResult<IReadOnlyList<PlayerPerformanceRecord>>> GetWeeklyPlayerStatsAsync(
        string season,
        int week,
        CancellationToken cancellationToken) =>
        GetCsvAsync(
            $"stats_player/stats_player_week_{season}.csv",
            (rows, observedAt) => ParsePlayerStats(rows, observedAt, season, week),
            cancellationToken);

    public Task<ProviderResult<IReadOnlyList<ExternalPlayerRecord>>> GetRostersAsync(
        string season,
        CancellationToken cancellationToken) =>
        GetCsvAsync(
            $"weekly_rosters/roster_weekly_{season}.csv",
            (rows, observedAt) => ParseRoster(rows, observedAt),
            cancellationToken);

    public Task<ProviderResult<IReadOnlyList<PlayerUsageRecord>>> GetUsageAsync(
        string season,
        CancellationToken cancellationToken) =>
        GetCsvAsync(
            $"stats_player/stats_player_week_{season}.csv",
            (rows, observedAt) => ParseUsage(rows, observedAt, season),
            cancellationToken);

    private async Task<ProviderResult<IReadOnlyList<T>>> GetCsvAsync<T>(
        string path,
        Func<IReadOnlyList<IReadOnlyDictionary<string, string>>, DateTimeOffset, IReadOnlyList<T>> parser,
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

        var sourceTimestamp = await GetSourceTimestampAsync(path, cancellationToken);
        if (sourceTimestamp is null)
        {
            return ProviderResult<IReadOnlyList<T>>.Failure(
                ProviderAvailability.Unavailable,
                Name,
                DateTimeOffset.UtcNow,
                "SOURCE_TIMESTAMP_UNAVAILABLE",
                "The nflverse source timestamp could not be retrieved.");
        }

        var observedAt = sourceTimestamp.Value;
        var freshUntil = observedAt.Add(CacheTtl);

        try
        {
            var records = parser(ParseCsv(response.Value), observedAt);
            logger.LogInformation("Provider {Provider} resource {Resource} produced {RecordCount} records.", Name, path, records.Count);
            return records.Count == 0
                ? ProviderResult<IReadOnlyList<T>>.EmptyResult(Name, observedAt, freshUntil)
                : ProviderResult<IReadOnlyList<T>>.AvailableResult(records, Name, observedAt, freshUntil);
        }
        catch (FormatException exception)
        {
            logger.LogWarning(exception, "Provider {Provider} resource {Resource} contained malformed CSV.", Name, path);
            return ProviderResult<IReadOnlyList<T>>.Failure(
                ProviderAvailability.Invalid,
                Name,
                observedAt,
                "MALFORMED_CSV",
                exception.Message);
        }
    }

    private async Task<DateTimeOffset?> GetSourceTimestampAsync(
        string resourcePath,
        CancellationToken cancellationToken)
    {
        var metadataPath = $"{GetReleaseTag(resourcePath)}/timestamp.json";
        var metadata = await GetTextAsync(metadataPath, cancellationToken);
        if (metadata.Availability != ProviderAvailability.Available || string.IsNullOrWhiteSpace(metadata.Value))
        {
            RemoveCachedText(metadataPath);
            logger.LogWarning(
                "Provider {Provider} source timestamp {Resource} returned {Availability} data.",
                Name,
                metadataPath,
                metadata.Availability);
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata.Value);
            var value = document.RootElement.TryGetProperty("last_updated", out var timestamp)
                ? timestamp.GetString()
                : null;
            value = value?
                .Replace(" UTC", " +00:00", StringComparison.OrdinalIgnoreCase)
                .Replace(" EST", " -05:00", StringComparison.OrdinalIgnoreCase)
                .Replace(" EDT", " -04:00", StringComparison.OrdinalIgnoreCase);
            if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
            {
                return parsed;
            }

            RemoveCachedText(metadataPath);
            return null;
        }
        catch (JsonException exception)
        {
            RemoveCachedText(metadataPath);
            logger.LogWarning(exception, "Provider {Provider} source timestamp {Resource} was malformed.", Name, metadataPath);
            return null;
        }
    }

    private static string GetReleaseTag(string resourcePath) =>
        resourcePath.Split('/', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
        ?? throw new ArgumentException("nflverse resource path must include a release tag.", nameof(resourcePath));

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
        DateTimeOffset observedAt,
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
                observedAt))
            .Where(x => x.PlayerId.Length > 0)
            .ToArray();

    private static IReadOnlyList<ExternalPlayerRecord> ParseRoster(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        DateTimeOffset observedAt) =>
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
                observedAt))
            .ToArray();

    private static IReadOnlyList<PlayerUsageRecord> ParseUsage(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        DateTimeOffset observedAt,
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
                observedAt))
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
