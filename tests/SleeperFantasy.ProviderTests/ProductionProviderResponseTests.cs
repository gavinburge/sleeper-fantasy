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
    public async Task Nflverse_production_stats_normalize_team_and_usage_fields()
    {
        var handler = new StaticResponseHandler(
            """
            player_id,player_name,position,season,week,season_type,team,receptions,targets,carries,fantasy_points,fantasy_points_ppr
            00-0000001,Fixture Player,RB,2026,10,REG,ABC,5,7,18,14.2,19.2
            """,
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
            """
            gsis_id,display_name,position,team_abbr,player_id
            00-0000002,Fixture & Player,WR,ABC,nfl-id-2
            """,
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
            """
            <section class="nfl-o-injury-report__unit">
              <div class="d3-o-section-sub-title"><span>Colts</span></div>
              <div class="d3-o-table--horizontal-scroll">
                <table class="d3-o-table d3-o-table--detailed d3-o-reports--detailed">
                  <thead><tr><th>Player</th><th>Position</th><th>Injuries</th><th>Practice Status</th><th>Game Status</th></tr></thead>
                  <tbody>
                    <tr>
                      <td><a href="/players/sauce-gardner/"> Sauce Gardner </a></td>
                      <td>CB</td>
                      <td>Calf</td>
                      <td>Did Not Participate In Practice</td>
                      <td>Questionable</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </section>
            """,
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
        Assert.Equal("Colts", record.Team);
        Assert.Equal("CB", record.Position);
        Assert.Equal("Calf", record.Injury);
        Assert.Equal("Did Not Participate In Practice", record.PracticeStatus);
        Assert.Equal(GameStatus.Questionable, record.Status);
    }

    [Fact]
    public async Task Nfl_html_parser_accepts_attribute_order_single_quotes_and_entities()
    {
        var handler = new StaticResponseHandler(
            """
            <section data-week='10' class='foo nfl-o-injury-report__unit'>
              <div class='d3-o-section-sub-title'><span>New &amp; England</span></div>
              <table>
                <tr><th>Player</th><th>Position</th><th>Injuries</th><th>Practice Status</th><th>Game Status</th></tr>
                <tr data-row='1'><td>Fixture &amp; Player</td><td>WR</td><td>Ankle</td><td>Limited</td><td>Out</td></tr>
              </table>
            </section>
            """,
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
