using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Infrastructure;
using SleeperFantasy.Infrastructure.Configuration;
using SleeperFantasy.Infrastructure.Providers;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class AnalysisReliabilityTests
{
    [Fact]
    public async Task Nflverse_usage_uses_the_latest_week_for_each_player()
    {
        var handler = new StaticHandler(
            """
            player_id,position,season,week,season_type,team,carries,receptions
            player-1,RB,2026,1,REG,ABC,10,2
            player-1,RB,2026,5,REG,ABC,20,7
            """,
            "text/csv");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nflverse.test/")
        };
        var provider = new NflverseDataProvider(
            client,
            Options.Create(new CacheOptions()),
            NullLogger<NflverseDataProvider>.Instance);

        var result = await provider.GetUsageAsync("2026", CancellationToken.None);

        var usage = Assert.Single(result.Value!);
        Assert.Equal(27m, usage.Usage["touches"]);
    }

    [Fact]
    public async Task Nflverse_roster_deduplicates_weekly_rows_by_external_identity()
    {
        var handler = new StaticHandler(
            """
            gsis_id,full_name,team,position,week
            player-1,Player One,ABC,RB,1
            player-1,Player One,ABC,RB,2
            """,
            "text/csv");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nflverse.test/")
        };
        var provider = new NflverseDataProvider(
            client,
            Options.Create(new CacheOptions()),
            NullLogger<NflverseDataProvider>.Instance);

        var result = await provider.GetRostersAsync("2026", CancellationToken.None);

        var roster = Assert.Single(result.Value!);
        Assert.Equal("player-1", roster.ProviderPlayerId);
    }

    [Fact]
    public async Task Roster_source_failure_preserves_compatible_ids_and_marks_identity_unavailable()
    {
        var fixture = new AnalysisFixture
        {
            FailExternalRoster = true,
            ExternalIds = new Dictionary<string, string>
            {
                ["rostered"] = "rostered",
                ["free"] = "free"
            }
        };

        var result = await fixture.CreateRefreshService().RefreshAsync(fixture.Configuration, CancellationToken.None);

        Assert.Contains(result.Performance, x => x.PlayerId == "rostered");
        Assert.Contains(result.Performance, x => x.PlayerId == "free");
        Assert.Contains(result.Usage, x => x.ProviderPlayerId == "free");
        Assert.Contains(result.Failures, x => x.Provider == "fixture-performance" && x.ErrorCode == "ROSTER_UNAVAILABLE");
        Assert.Contains(result.Evidence, x =>
            x.EvidenceType == "identity-unavailable"
            && x.Availability == ProviderAvailability.Unavailable);
    }

    [Fact]
    public async Task Sleeper_state_with_missing_week_is_invalid()
    {
        var handler = new StaticHandler("""{"sport":"nfl","season":"2026"}""", "application/json");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://sleeper.test/")
        };
        var provider = new SleeperDataProvider(
            client,
            Options.Create(new CacheOptions()),
            NullLogger<SleeperDataProvider>.Instance);

        var result = await provider.GetStateAsync(CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(ProviderAvailability.Invalid, result.Availability);
        Assert.Equal("INVALID_REQUIRED_WEEK", result.ErrorCode);
    }

    [Fact]
    public async Task Sleeper_settings_with_invalid_json_shapes_are_invalid()
    {
        var handler = new StaticHandler(
            """{"scoring_settings":[],"roster_positions":{}}""",
            "application/json");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://sleeper.test/")
        };
        var provider = new SleeperDataProvider(
            client,
            Options.Create(new CacheOptions()),
            NullLogger<SleeperDataProvider>.Instance);

        var result = await provider.GetSettingsAsync("league-1", CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(ProviderAvailability.Invalid, result.Availability);
        Assert.Equal("INVALID_SETTINGS", result.ErrorCode);
    }

    [Fact]
    public async Task Gemini_retries_rate_limits_and_parses_nullable_optional_fields()
    {
        var handler = new GeminiHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            JsonResponse("""
                {"candidates":[{"content":{"parts":[{"text":"{\"summary\":null,\"findings\":[],\"conflicts\":[]}"}]}}]}
                """));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gemini.test/v1beta/")
        };
        var provider = new GeminiLanguageModelProvider(
            client,
            Options.Create(new ProviderOptions
            {
                GeminiApiKey = "key",
                GeminiModel = "test-model",
                LanguageModelTimeoutSeconds = 5,
                LanguageModelMaxRetries = 1,
                LanguageModelRetryDelayMilliseconds = 0
            }),
            NullLogger<GeminiLanguageModelProvider>.Instance);

        var result = await provider.AnalyzeAsync(
            new LanguageModelRequest("analysis", "system", "user", []),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(2, handler.RequestCount);
        Assert.Empty(result.Value!.Findings);
        Assert.Empty(result.Value.Conflicts);
    }

    [Theory]
    [InlineData("""{"summary":"ok","conflicts":[]}""")]
    [InlineData("""{"summary":"ok","findings":[]}""")]
    public async Task Gemini_returns_invalid_when_required_response_properties_are_missing(string modelJson)
    {
        var handler = new GeminiHandler(
            JsonResponse(
                """{"candidates":[{"content":{"parts":[{"text":"""
                + JsonSerializer.Serialize(modelJson)
                + """}]}}]}"""));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gemini.test/v1beta/")
        };
        var provider = new GeminiLanguageModelProvider(
            client,
            Options.Create(new ProviderOptions
            {
                GeminiApiKey = "key",
                GeminiModel = "test-model",
                LanguageModelTimeoutSeconds = 5,
                LanguageModelMaxRetries = 0
            }),
            NullLogger<GeminiLanguageModelProvider>.Instance);

        var result = await provider.AnalyzeAsync(
            new LanguageModelRequest("analysis", "system", "user", []),
            CancellationToken.None);

        Assert.Equal(ProviderAvailability.Invalid, result.Availability);
        Assert.Equal("INVALID_MODEL_RESPONSE", result.ErrorCode);
    }

    [Fact]
    public async Task Gemini_returns_invalid_for_malformed_model_json()
    {
        var handler = new GeminiHandler(
            JsonResponse("""
                {"candidates":[{"content":{"parts":[{"text":"not json"}]}}]}
                """));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gemini.test/v1beta/")
        };
        var provider = new GeminiLanguageModelProvider(
            client,
            Options.Create(new ProviderOptions
            {
                GeminiApiKey = "key",
                GeminiModel = "test-model",
                LanguageModelTimeoutSeconds = 5,
                LanguageModelMaxRetries = 0
            }),
            NullLogger<GeminiLanguageModelProvider>.Instance);

        var result = await provider.AnalyzeAsync(
            new LanguageModelRequest("analysis", "system", "user", []),
            CancellationToken.None);

        Assert.Equal(ProviderAvailability.Invalid, result.Availability);
        Assert.Equal("INVALID_MODEL_RESPONSE", result.ErrorCode);
    }

    [Fact]
    public async Task Gemini_retries_server_errors_then_succeeds()
    {
        var handler = new GeminiHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            JsonResponse("""
                {"candidates":[{"content":{"parts":[{"text":"{\"summary\":\"ok\",\"findings\":[],\"conflicts\":[]}"}]}}]}
                """));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gemini.test/v1beta/")
        };
        var provider = new GeminiLanguageModelProvider(
            client,
            Options.Create(new ProviderOptions
            {
                GeminiApiKey = "key",
                GeminiModel = "test-model",
                LanguageModelTimeoutSeconds = 5,
                LanguageModelMaxRetries = 1,
                LanguageModelRetryDelayMilliseconds = 0
            }),
            NullLogger<GeminiLanguageModelProvider>.Instance);

        var result = await provider.AnalyzeAsync(
            new LanguageModelRequest("analysis", "system", "user", []),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Gemini_does_not_retry_permanent_client_errors()
    {
        var handler = new GeminiHandler(new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gemini.test/v1beta/")
        };
        var provider = new GeminiLanguageModelProvider(
            client,
            Options.Create(new ProviderOptions
            {
                GeminiApiKey = "key",
                GeminiModel = "test-model",
                LanguageModelTimeoutSeconds = 5,
                LanguageModelMaxRetries = 3,
                LanguageModelRetryDelayMilliseconds = 0
            }),
            NullLogger<GeminiLanguageModelProvider>.Instance);

        var result = await provider.AnalyzeAsync(
            new LanguageModelRequest("analysis", "system", "user", []),
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(ProviderAvailability.Unavailable, result.Availability);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Gemini_retries_an_attempt_timeout()
    {
        var handler = new TimeoutThenSuccessHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gemini.test/v1beta/")
        };
        var provider = new GeminiLanguageModelProvider(
            client,
            Options.Create(new ProviderOptions
            {
                GeminiApiKey = "key",
                GeminiModel = "test-model",
                LanguageModelTimeoutSeconds = 1,
                LanguageModelMaxRetries = 1,
                LanguageModelRetryDelayMilliseconds = 0
            }),
            NullLogger<GeminiLanguageModelProvider>.Instance);

        var result = await provider.AnalyzeAsync(
            new LanguageModelRequest("analysis", "system", "user", []),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Gemini_keeps_prompt_within_configured_limit()
    {
        var handler = new GeminiHandler(
            JsonResponse("""
                {"candidates":[{"content":{"parts":[{"text":"{\"summary\":\"ok\",\"findings\":[],\"conflicts\":[]}"}]}}]}
                """));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gemini.test/v1beta/")
        };
        var provider = new GeminiLanguageModelProvider(
            client,
            Options.Create(new ProviderOptions
            {
                GeminiApiKey = "key",
                GeminiModel = "test-model",
                LanguageModelTimeoutSeconds = 5,
                LanguageModelMaxPromptCharacters = 256,
                LanguageModelMaxRetries = 0
            }),
            NullLogger<GeminiLanguageModelProvider>.Instance);

        var request = new LanguageModelRequest(
            "analysis",
            new string('s', 200),
            new string('u', 200),
            Enumerable.Range(1, 10)
                .Select(index => new LanguageModelEvidence(
                    $"evidence-{index}",
                    $"subject-{index}",
                    new string('f', 100),
                    "fixture",
                    DateTimeOffset.UtcNow,
                    null,
                    1m))
                .ToArray());

        var result = await provider.AnalyzeAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        using var sent = JsonDocument.Parse(handler.RequestBodies.Single());
        var prompt = sent.RootElement
            .GetProperty("contents")[0]
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
        Assert.NotNull(prompt);
        Assert.InRange(prompt!.Length, 1, 256);
    }

    [Fact]
    public void Analysis_engine_is_registered_in_production_di()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["Providers:SleeperBaseUrl"] = "https://sleeper.test",
                ["Providers:NflverseBaseUrl"] = "https://nflverse.test",
                ["Providers:InjuryReportBaseUrl"] = "https://injury.test",
                ["Providers:GeminiBaseUrl"] = "https://gemini.test/v1beta",
                ["Database:ApplyMigrations"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<AnalysisOrchestrator>());
    }

    private static HttpResponseMessage JsonResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StaticHandler(string body, string mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType)
            });
    }

    private sealed class GeminiHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responseQueue = new(responses);
        public int RequestCount { get; private set; }
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return responseQueue.Count > 1
                ? responseQueue.Dequeue()
                : responseQueue.Single();
        }
    }

    private sealed class TimeoutThenSuccessHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

            return JsonResponse("""
                {"candidates":[{"content":{"parts":[{"text":"{\"summary\":\"ok\",\"findings\":[],\"conflicts\":[]}"}]}}]}
                """);
        }
    }
}
