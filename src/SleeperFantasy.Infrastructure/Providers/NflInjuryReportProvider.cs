using System.Text.Json;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Infrastructure.Configuration;

namespace SleeperFantasy.Infrastructure.Providers;

public sealed class NflInjuryReportProvider(
    HttpClient httpClient,
    IOptions<CacheOptions> cacheOptions,
    ILogger<NflInjuryReportProvider> logger)
    : ProviderHttpClient(httpClient, cacheOptions, logger), IInjuryDataProvider
{
    public const string Name = "nfl.com-injury-report";
    public string ProviderName => Name;

    public async Task<ProviderResult<IReadOnlyList<InjuryReportRecord>>> GetCurrentReportsAsync(
        CancellationToken cancellationToken)
    {
        var response = await GetTextAsync(string.Empty, cancellationToken);
        if (response.Availability != ProviderAvailability.Available || response.Value is null)
        {
            return ProviderResult<IReadOnlyList<InjuryReportRecord>>.Failure(
                response.Availability,
                Name,
                response.ObservedAt,
                response.Availability.ToString().ToUpperInvariant(),
                $"The {Name} provider returned {response.Availability.ToString().ToLowerInvariant()} data.");
        }

        try
        {
            var sourceUrl = BaseAddress?.ToString() ?? string.Empty;
            var records = Parse(response.Value, sourceUrl, response.ObservedAt);
            return records.Count == 0
                ? ProviderResult<IReadOnlyList<InjuryReportRecord>>.EmptyResult(
                    Name, response.ObservedAt, response.FreshUntil)
                : ProviderResult<IReadOnlyList<InjuryReportRecord>>.AvailableResult(
                    records, Name, response.ObservedAt, response.FreshUntil);
        }
        catch (JsonException exception)
        {
            return ProviderResult<IReadOnlyList<InjuryReportRecord>>.Failure(
                ProviderAvailability.Invalid, Name, response.ObservedAt, "MALFORMED_RESPONSE", exception.Message);
        }
    }

    private static IReadOnlyList<InjuryReportRecord> Parse(
        string body,
        string sourceUrl,
        DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        if (body.TrimStart().StartsWith('{') || body.TrimStart().StartsWith('['))
        {
            using var document = JsonDocument.Parse(body);
            return ParseJson(document.RootElement, sourceUrl, observedAt);
        }

        var records = new List<InjuryReportRecord>();
        var cellPattern = new Regex(
            "<t[dh][^>]*>(?<value>.*?)</t[dh]>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        var sectionPattern = new Regex(
            "<section[^>]*class\\s*=\\s*[\"'][^\"']*nfl-o-injury-report__unit[^\"']*[\"'][^>]*>(?<body>.*?)(?=<section[^>]*class\\s*=\\s*[\"'][^\"']*nfl-o-injury-report__unit|</main>|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        var tablePattern = new Regex(
            "<table[^>]*>(?<table>.*?)</table>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        var rowPattern = new Regex(
            "<tr[^>]*>(?<cells>.*?)</tr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        foreach (Match section in sectionPattern.Matches(body))
        {
            var team = ExtractTeam(section.Groups["body"].Value);
            var table = tablePattern.Match(section.Groups["body"].Value);
            if (!table.Success)
            {
                continue;
            }

            foreach (Match row in rowPattern.Matches(table.Groups["table"].Value))
            {
                var cells = cellPattern.Matches(row.Groups["cells"].Value)
                    .Select(x => WebUtility.HtmlDecode(Regex.Replace(x.Groups["value"].Value, "<.*?>", string.Empty).Trim()))
                    .ToArray();
                if (cells.Length < 5 || cells[0].Equals("Player", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                records.Add(new InjuryReportRecord(
                    cells[0],
                    cells[0],
                    team,
                    cells[1],
                    cells[2],
                    cells[3],
                    ParseStatus(cells[4]),
                    observedAt,
                    sourceUrl));
            }
        }

        return records;
    }

    private static string? ExtractTeam(string section)
    {
        var match = Regex.Match(
            section,
            "d3-o-section-sub-title[^>]*>\\s*<span[^>]*>\\s*(?<team>[^<]+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["team"].Value.Trim()) : null;
    }

    private static IReadOnlyList<InjuryReportRecord> ParseJson(
        JsonElement root,
        string sourceUrl,
        DateTimeOffset observedAt)
    {
        var items = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray()
            : root.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array
                ? players.EnumerateArray()
                : [];
        return items
            .Select(item =>
            {
                var name = item.GetOptionalString("fullName") ?? item.GetOptionalString("playerName") ?? item.GetOptionalString("name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                return new InjuryReportRecord(
                    item.GetOptionalString("gsisId") ?? item.GetOptionalString("playerId") ?? name,
                    name,
                    item.GetOptionalString("team"),
                    item.GetOptionalString("position"),
                    item.GetOptionalString("injury") ?? item.GetOptionalString("bodyPart"),
                    item.GetOptionalString("practiceStatus"),
                    ParseStatus(item.GetOptionalString("gameStatus") ?? item.GetOptionalString("status")),
                    observedAt,
                    sourceUrl);
            })
            .Where(x => x is not null)
            .Cast<InjuryReportRecord>()
            .ToArray();
    }

    private static GameStatus ParseStatus(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "active" or "full participation" => GameStatus.Active,
            "questionable" or "limited" => GameStatus.Questionable,
            "doubtful" => GameStatus.Doubtful,
            "out" or "did not participate" => GameStatus.Out,
            "ir" or "injured reserve" => GameStatus.InjuredReserve,
            "suspended" => GameStatus.Suspended,
            _ => GameStatus.Unknown
        };
}

internal static class JsonElementProviderExtensions
{
    public static string? GetOptionalString(this JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
