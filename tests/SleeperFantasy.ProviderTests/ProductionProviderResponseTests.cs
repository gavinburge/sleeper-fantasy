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
    public async Task Nflverse_production_stats_normalize_team_and_usage_fields()
    {
        var handler = new StaticResponseHandler(
            Fixture("nflverse-stats.csv"),
            "text/csv");
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
        Assert.Equal(18m, usageRecord.Usage["carries"]);
        Assert.Equal(5m, usageRecord.Usage["receptions"]);
        Assert.Equal(23m, usageRecord.Usage["touches"]);
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

    private sealed class StaticResponseHandler(string body, string mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType)
            });
    }
}
