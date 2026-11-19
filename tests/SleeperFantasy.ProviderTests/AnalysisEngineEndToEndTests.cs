using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Domain.Entities;
using SleeperFantasy.Infrastructure.Configuration;
using SleeperFantasy.Infrastructure.Providers;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class AnalysisEngineEndToEndTests
{
    [Fact]
    public async Task Runs_against_stubbed_sources_and_sends_exact_deterministic_gemini_request()
    {
        var sourceHandler = new AnalysisApiStubHandler();
        var geminiHandler = new GeminiCaptureHandler();
        var cacheOptions = Options.Create(new CacheOptions { DefaultTtlHours = 24, MaxEntriesPerProvider = 100 });
        var providerOptions = Options.Create(new ProviderOptions
        {
            GeminiApiKey = "fixture-gemini-key",
            GeminiModel = "gemini-2.0-flash",
            LanguageModelTimeoutSeconds = 10,
            LanguageModelMaxPromptCharacters = 24_000
        });

        using var sleeperClient = new HttpClient(sourceHandler) { BaseAddress = new Uri("https://sleeper.test/") };
        using var nflverseClient = new HttpClient(sourceHandler) { BaseAddress = new Uri("https://nflverse.test/") };
        using var injuryClient = new HttpClient(sourceHandler) { BaseAddress = new Uri("https://injury.test/") };
        using var geminiClient = new HttpClient(geminiHandler) { BaseAddress = new Uri("https://gemini.test/v1beta/") };

        var leagueProvider = new SleeperDataProvider(
            sleeperClient,
            cacheOptions,
            NullLogger<SleeperDataProvider>.Instance);
        var performanceProvider = new NflverseDataProvider(
            nflverseClient,
            cacheOptions,
            NullLogger<NflverseDataProvider>.Instance);
        var injuryProvider = new NflInjuryReportProvider(
            injuryClient,
            cacheOptions,
            NullLogger<NflInjuryReportProvider>.Instance);
        var languageModelProvider = new GeminiLanguageModelProvider(
            geminiClient,
            providerOptions,
            NullLogger<GeminiLanguageModelProvider>.Instance);
        var fixture = new AnalysisFixture();
        var orchestrator = new AnalysisOrchestrator(
            new SourceRefreshService(leagueProvider, performanceProvider, injuryProvider),
            new RosterRiskAnalyzer(),
            new AvailablePlayerService(),
            new WaiverRankingService(),
            new LanguageModelAnalysisService(languageModelProvider));

        var first = await orchestrator.AnalyzeAsync(fixture.Configuration, CancellationToken.None);
        var second = await orchestrator.AnalyzeAsync(fixture.Configuration, CancellationToken.None);

        Assert.Equal(2, geminiHandler.RequestBodies.Count);
        Assert.Equal("/v1beta/models/gemini-2.0-flash:generateContent", geminiHandler.RequestUris[0]);
        Assert.Equal("fixture-gemini-key", geminiHandler.ApiKeys[0]);
        Assert.Equal(82m, Assert.Single(first.Deterministic.Waivers.Recommendations).Score);
        Assert.Equal("free", first.Deterministic.Waivers.Recommendations[0].PlayerId);
        Assert.Contains("injury", first.Deterministic.RosterRisk.Players.Single(x => x.PlayerId == "rostered").RiskCodes);
        Assert.Equal(
            first.Deterministic.Waivers.Recommendations.Select(x => (x.PlayerId, x.Score, x.Priority, x.Confidence)),
            second.Deterministic.Waivers.Recommendations.Select(x => (x.PlayerId, x.Score, x.Priority, x.Confidence)));
        Assert.Equal(
            first.Deterministic.RosterRisk.Players.Select(x => (x.PlayerId, x.RiskCodes, x.UnavailableSignals)),
            second.Deterministic.RosterRisk.Players.Select(x => (x.PlayerId, x.RiskCodes, x.UnavailableSignals)));

        AssertExactGeminiRequest(geminiHandler.RequestBodies[0]);
        AssertExactGeminiRequest(geminiHandler.RequestBodies[1], allowFreshnessTimestamps: true);
    }

    private static void AssertExactGeminiRequest(
        string body,
        bool allowFreshnessTimestamps = false)
    {
        using var requestDocument = JsonDocument.Parse(body);
        var requestRoot = requestDocument.RootElement;
        Assert.Equal("application/json", requestRoot.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
        Assert.Equal(0.1m, requestRoot.GetProperty("generationConfig").GetProperty("temperature").GetDecimal());
        var prompt = requestRoot.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
        if (allowFreshnessTimestamps)
        {
            Assert.StartsWith("Explain only the supplied evidence and deterministic summary.", prompt);
        }
        else
        {
            Assert.StartsWith("Explain only the supplied evidence and deterministic summary.", prompt);
        }

        Assert.Contains("Deterministic summary (authoritative; do not override):", prompt);
        Assert.Contains("rosterRisks", prompt);
        Assert.Contains("waivers", prompt);
        Assert.Contains("availablePlayers", prompt);
        Assert.Contains("Sleeper:rosters:league-1:roster-snapshot", prompt);
        Assert.Contains("Sleeper:rosters:league-1:roster-snapshot", prompt);
        Assert.Contains("nflverse:performance:weekly-performance-4:performance-snapshot", prompt);
        Assert.Contains("\\u0022fantasy_points\\u0022:12", prompt);
        Assert.Contains("\\u0022touches\\u0022:23", prompt);
    }

    private sealed class AnalysisApiStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var host = request.RequestUri!.Host;
            var path = request.RequestUri.AbsolutePath.Trim('/');
            var response = host switch
            {
                "sleeper.test" => Sleeper(path),
                "nflverse.test" => Nflverse(path),
                "injury.test" => Injury(),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Sleeper(string path)
        {
            object payload = path switch
            {
                "v1/state/nfl" => new
                {
                    sport = "nfl",
                    season = "2026",
                    week = "5",
                    season_type = "regular",
                    display_week = "5"
                },
                "v1/league/league-1" => new
                {
                    league_id = "league-1",
                    name = "Fixture League",
                    season = "2026",
                    status = "in_season",
                    sport = "nfl",
                    total_rosters = 2,
                    scoring_settings = new { rec = 1 },
                    roster_positions = new[] { "RB", "WR", "WR" },
                    settings = new { leg = 5 }
                },
                "v1/league/league-1/rosters" => new[]
                {
                    new
                    {
                        roster_id = "roster-1",
                        owner_id = "sleeper-user",
                        players = new[] { "rostered" },
                        starters = new[] { "rostered" },
                        settings = new { wins = 3, losses = 1, ties = 0, fpts = 100.5m },
                        metadata = new { team_name = "Home" }
                    },
                    new
                    {
                        roster_id = "roster-2",
                        owner_id = "other-user",
                        players = Array.Empty<string>(),
                        starters = Array.Empty<string>(),
                        settings = new { wins = 1, losses = 3, ties = 0, fpts = 80m },
                        metadata = new { team_name = "Away" }
                    }
                },
                "v1/league/league-1/matchups/5" => Array.Empty<object>(),
                "v1/league/league-1/transactions/5" => Array.Empty<object>(),
                "v1/players/nfl/trending/add" => Array.Empty<object>(),
                "v1/players/nfl" => new
                {
                    rostered = new
                    {
                        full_name = "Rostered Player",
                        first_name = "Rostered",
                        last_name = "Player",
                        position = "RB",
                        team = "ABC",
                        active = true
                    },
                    free = new
                    {
                        full_name = "Free Player",
                        first_name = "Free",
                        last_name = "Player",
                        position = "RB",
                        team = "ABC",
                        active = true
                    }
                },
                _ => new { error = "not found" }
            };
            return JsonResponse(payload);
        }

        private static HttpResponseMessage Nflverse(string path)
        {
            var csv = path switch
            {
                "schedules/games.csv" =>
                    "game_id,season,week,gameday,home_team,away_team,game_type\n"
                    + "g1,2026,5,2026-10-10,ABC,DEF,REG\n",
                "stats_player/stats_player_week_2026.csv" =>
                    "player_id,position,season,week,season_type,team,receptions,targets,carries,fantasy_points,fantasy_points_ppr\n"
                    + "00-ROSTERED,RB,2026,4,REG,ABC,2,3,15,10,12\n"
                    + "00-FREE,WR,2026,4,REG,ABC,5,7,18,12,17\n"
                    + "00-ROSTERED,RB,2026,5,REG,ABC,3,4,16,11,14\n",
                "weekly_rosters/roster_weekly_2026.csv" =>
                    "gsis_id,full_name,team,position,week\n"
                    + "00-ROSTERED,Rostered Player,XYZ,RB,4\n"
                    + "00-ROSTERED,Rostered Player,ABC,RB,5\n"
                    + "00-FREE,Free Player,ABC,WR,5\n",
                _ => string.Empty
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(csv, Encoding.UTF8, "text/csv")
            };
        }

        private static HttpResponseMessage Injury() =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    <section class="nfl-o-injury-report__unit">
                      <div class="d3-o-section-sub-title"><span>ABC</span></div>
                      <table>
                        <thead><tr><th>Player</th><th>Position</th><th>Injuries</th><th>Practice Status</th><th>Game Status</th></tr></thead>
                        <tbody><tr><td>Rostered Player</td><td>RB</td><td>Knee</td><td>Limited Participation in Practice</td><td>Out</td></tr></tbody>
                      </table>
                    </section>
                    """,
                    Encoding.UTF8,
                    "text/html")
            };

        private static HttpResponseMessage JsonResponse(object payload) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };
    }

    private sealed class GeminiCaptureHandler : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = [];
        public List<string> RequestUris { get; } = [];
        public List<string> ApiKeys { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            RequestUris.Add(request.RequestUri!.AbsolutePath);
            ApiKeys.Add(request.Headers.GetValues("x-goog-api-key").Single());
            var modelResponse = new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[]
                            {
                                new
                                {
                                    text = """
                                    {"summary":"Fixture explanation","findings":[],"conflicts":[]}
                                    """
                                }
                            }
                        }
                    }
                }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(modelResponse),
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
