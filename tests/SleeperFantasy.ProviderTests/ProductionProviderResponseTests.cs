using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Infrastructure.Configuration;
using SleeperFantasy.Infrastructure.Providers;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class ProductionProviderResponseTests
{
    [Fact]
    public async Task Sleeper_public_shape_fixture_loads_user()
    {
        var handler = new StaticResponseHandler(Fixture("sleeper-user.json"), "application/json");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.sleeper.app/") };
        var provider = new SleeperDataProvider(client, Options.Create(new CacheOptions()), NullLogger<SleeperDataProvider>.Instance);

        var result = await provider.GetUserAsync("fixture-user", CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal("fixture-user-id", result.Value?.UserId);
    }

    [Fact]
    public async Task Sleeper_result_uses_source_specific_ttl()
    {
        var handler = new StaticResponseHandler(Fixture("sleeper-user.json"), "application/json");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.sleeper.app/") };
        var provider = new SleeperDataProvider(
            client,
            Options.Create(new CacheOptions { SleeperTtlHours = 2 }),
            NullLogger<SleeperDataProvider>.Instance);

        var result = await provider.GetUserAsync("fixture-user", CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(2), result.FreshUntil - result.ObservedAt);
    }

    [Fact]
    public async Task Sleeper_transaction_object_maps_player_ids_from_object_keys()
    {
        var handler = new StaticResponseHandler(Fixture("sleeper-transactions.json"), "application/json");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.sleeper.app/") };
        var provider = new SleeperDataProvider(client, Options.Create(new CacheOptions()), NullLogger<SleeperDataProvider>.Instance);

        var result = await provider.GetTransactionsAsync("league-1", 10, CancellationToken.None);

        var transaction = Assert.Single(result.Value!);
        Assert.Equal(["player-add-1", "player-add-2"], transaction.Adds);
        Assert.Equal(["player-drop-1"], transaction.Drops);
    }

    [Fact]
    public async Task Provider_cache_is_shared_across_transient_provider_instances()
    {
        var handler = new CountingJsonHandler(Fixture("sleeper-user.json"));
        var cache = new ProviderResponseCache();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.sleeper.app/") };
        var options = Options.Create(new CacheOptions());
        var first = new SleeperDataProvider(client, options, NullLogger<SleeperDataProvider>.Instance, cache);
        var second = new SleeperDataProvider(client, options, NullLogger<SleeperDataProvider>.Instance, cache);

        await first.GetUserAsync("fixture-user", CancellationToken.None);
        await second.GetUserAsync("fixture-user", CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Nflverse_production_stats_normalize_team_and_usage_fields()
    {
        var handler = new StaticResponseHandler(
            Fixture("nflverse-stats.csv"),
            "text/csv",
            """{"last_updated":"2026-11-19 15:00:00 UTC"}""");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://github.com/nflverse/nflverse-data/releases/download/")
        };
        var provider = new NflverseDataProvider(
            client,
            Options.Create(new CacheOptions()),
            NullLogger<NflverseDataProvider>.Instance);

        var performance = await provider.GetWeeklyPlayerStatsAsync("2026", 10, CancellationToken.None);
        var usage = await provider.GetUsageAsync("2026", CancellationToken.None);

        var performanceRecord = Assert.Single(performance.Value!);
        var usageRecord = Assert.Single(usage.Value!);
        Assert.Equal("ABC", performanceRecord.Team);
        Assert.Equal(14.2m, performanceRecord.Statistics["fantasy_points"]);
        Assert.Equal(DateTimeOffset.Parse("2026-11-19 15:00:00 +00:00"), performanceRecord.ObservedAt);
        Assert.Equal(18m, usageRecord.Usage["carries"]);
        Assert.Equal(5m, usageRecord.Usage["receptions"]);
        Assert.Equal(23m, usageRecord.Usage["touches"]);
        Assert.Equal(DateTimeOffset.Parse("2026-11-19 15:00:00 +00:00"), usageRecord.ObservedAt);
        Assert.Contains("/nflverse/nflverse-data/releases/download/stats_player/stats_player_week_2026.csv", handler.RequestPaths);
        Assert.Contains("/nflverse/nflverse-data/releases/download/stats_player/timestamp.json", handler.RequestPaths);
    }

    [Fact]
    public async Task Nflverse_roster_payload_preserves_external_identity_fields()
    {
        var handler = new StaticResponseHandler(
            Fixture("nflverse-roster.csv"),
            "text/csv");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://github.com/nflverse/nflverse-data/releases/download/")
        };
        var provider = new NflverseDataProvider(
            client,
            Options.Create(new CacheOptions()),
            NullLogger<NflverseDataProvider>.Instance);

        var result = await provider.GetRostersAsync("2026", CancellationToken.None);

        var record = Assert.Single(result.Value!);
        Assert.Equal("00-0000002", record.ProviderPlayerId);
        Assert.Equal("00-0000002", record.GsisId);
        Assert.Equal("Fixture & Player", record.FullName);
        Assert.Equal("ABC", record.Team);
        Assert.Equal(DateTimeOffset.Parse("2026-11-19 00:00:00 +00:00"), record.ObservedAt);
        Assert.Contains("/nflverse/nflverse-data/releases/download/weekly_rosters/roster_weekly_2026.csv", handler.RequestPaths);
        Assert.Contains("/nflverse/nflverse-data/releases/download/weekly_rosters/timestamp.json", handler.RequestPaths);
    }

    [Fact]
    public async Task Nflverse_result_uses_source_timestamp_for_freshness()
    {
        var sourceTimestamp = "2026-11-18 12:00:00 UTC";
        var handler = new StaticResponseHandler(
            Fixture("nflverse-stats.csv"),
            "text/csv",
            $$"""{"last_updated":"{{sourceTimestamp}}"}""");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://nflverse.test/") };
        var provider = new NflverseDataProvider(
            client,
            Options.Create(new CacheOptions { NflverseTtlHours = 3 }),
            NullLogger<NflverseDataProvider>.Instance);

        var result = await provider.GetWeeklyPlayerStatsAsync("2026", 10, CancellationToken.None);

        var expectedObservedAt = DateTimeOffset.Parse("2026-11-18 12:00:00 +00:00");
        Assert.Equal(expectedObservedAt, result.ObservedAt);
        Assert.Equal(expectedObservedAt.AddHours(3), result.FreshUntil);
        Assert.Contains("/stats_player/timestamp.json", handler.RequestPaths);
    }

    [Fact]
    public async Task Nflverse_stale_source_timestamp_is_preserved_in_result_metadata()
    {
        var handler = new StaticResponseHandler(
            Fixture("nflverse-stats.csv"),
            "text/csv",
            """{"last_updated":"2020-01-01 00:00:00 UTC"}""");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://nflverse.test/") };
        var provider = new NflverseDataProvider(client, Options.Create(new CacheOptions()), NullLogger<NflverseDataProvider>.Instance);

        var result = await provider.GetWeeklyPlayerStatsAsync("2026", 10, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(DateTimeOffset.Parse("2020-01-01 00:00:00 +00:00"), result.ObservedAt);
        Assert.True(result.FreshUntil < DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Sleeper_malformed_json_returns_stable_invalid_result()
    {
        var handler = new StaticResponseHandler("{not-json", "application/json");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.sleeper.app/") };
        var provider = new SleeperDataProvider(client, Options.Create(new CacheOptions()), NullLogger<SleeperDataProvider>.Instance);

        var result = await provider.GetUserAsync("fixture-user", CancellationToken.None);

        Assert.Equal(ProviderAvailability.Invalid, result.Availability);
        Assert.Equal("INVALID_JSON", result.ErrorCode);
        Assert.Equal("The Sleeper provider returned malformed or schema-invalid JSON.", result.ErrorMessage);
    }

    [Fact]
    public async Task Nflverse_result_is_unavailable_when_source_timestamp_fails()
    {
        var handler = new FailingMetadataHandler(Fixture("nflverse-stats.csv"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://nflverse.test/") };
        var provider = new NflverseDataProvider(
            client,
            Options.Create(new CacheOptions()),
            NullLogger<NflverseDataProvider>.Instance);

        var result = await provider.GetWeeklyPlayerStatsAsync("2026", 10, CancellationToken.None);

        Assert.Equal(ProviderAvailability.Unavailable, result.Availability);
        Assert.Equal("SOURCE_TIMESTAMP_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task Nfl_html_production_report_maps_team_and_status_columns()
    {
        var handler = new StaticResponseHandler(
            Fixture("nfl-injury-report.html"),
            "text/html");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.nfl.com/injuries/")
        };
        var provider = new NflInjuryReportProvider(
            client,
            Options.Create(new CacheOptions()),
            NullLogger<NflInjuryReportProvider>.Instance);

        var result = await provider.GetCurrentReportsAsync(CancellationToken.None);

        var record = Assert.Single(result.Value!);
        Assert.Equal("New & England", record.Team);
        Assert.Equal("WR", record.Position);
        Assert.Equal("Ankle", record.Injury);
        Assert.Equal("Limited", record.PracticeStatus);
        Assert.Equal(GameStatus.Out, record.Status);
    }

    [Fact]
    public async Task Nfl_injury_report_result_uses_source_specific_ttl()
    {
        var handler = new StaticResponseHandler(
            Fixture("nfl-injury-report.html"),
            "text/html");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.nfl.com/injuries/")
        };
        var provider = new NflInjuryReportProvider(
            client,
            Options.Create(new CacheOptions { InjuryReportTtlHours = 4 }),
            NullLogger<NflInjuryReportProvider>.Instance);

        var result = await provider.GetCurrentReportsAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(4), result.FreshUntil - result.ObservedAt);
    }

    [Fact]
    public async Task Nfl_html_parser_accepts_attribute_order_single_quotes_and_entities()
    {
        var handler = new StaticResponseHandler(
            "<section data-week='10' class='foo nfl-o-injury-report__unit'><div class='d3-o-section-sub-title'><span>New &amp; England</span></div><table><tr><th>Player</th><th>Position</th><th>Injuries</th><th>Practice Status</th><th>Game Status</th></tr><tr data-row='1'><td>Fixture &amp; Player</td><td>WR</td><td>Ankle</td><td>Limited</td><td>Out</td></tr></table></section>",
            "text/html");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.nfl.com/injuries/")
        };
        var provider = new NflInjuryReportProvider(
            client,
            Options.Create(new CacheOptions()),
            NullLogger<NflInjuryReportProvider>.Instance);

        var result = await provider.GetCurrentReportsAsync(CancellationToken.None);

        var record = Assert.Single(result.Value!);
        Assert.Equal("New & England", record.Team);
        Assert.Equal("Fixture & Player", record.PlayerName);
        Assert.Equal(GameStatus.Out, record.Status);
    }

    [Fact]
    public async Task Nfl_html_parser_returns_empty_for_unsupported_section_shape()
    {
        var handler = new StaticResponseHandler(
            "<main><section class='nfl-o-injury-report__unit'><h2>No report</h2></section></main>",
            "text/html");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://www.nfl.com/injuries/") };
        var provider = new NflInjuryReportProvider(client, Options.Create(new CacheOptions()), NullLogger<NflInjuryReportProvider>.Instance);

        var result = await provider.GetCurrentReportsAsync(CancellationToken.None);

        Assert.Equal(ProviderAvailability.Empty, result.Availability);
        Assert.Empty(result.Value ?? []);
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private sealed class StaticResponseHandler(
        string body,
        string mediaType,
        string? timestampBody = null) : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(AddRequest(request));

        private HttpResponseMessage AddRequest(HttpRequestMessage request)
        {
            RequestPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri?.AbsolutePath.EndsWith("timestamp.json", StringComparison.OrdinalIgnoreCase) == true
                        ? timestampBody ?? """{"last_updated":"2026-11-19 00:00:00 UTC"}"""
                        : body,
                    Encoding.UTF8,
                    request.RequestUri?.AbsolutePath.EndsWith("timestamp.json", StringComparison.OrdinalIgnoreCase) == true
                        ? "application/json"
                        : mediaType)
            };
        }
    }

    private sealed class FailingMetadataHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri?.AbsolutePath.EndsWith("timestamp.json", StringComparison.OrdinalIgnoreCase) == true
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/csv")
                });
    }

    private sealed class CountingJsonHandler(string body) : HttpMessageHandler
    {
        private int requestCount;
        public int RequestCount => requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
