using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class AnalysisLifecycleEndToEndTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17")
        .Build();

    private AnalysisFixture fixture = null!;
    private CapturingTrigger trigger = null!;
    private ApiFactory factory = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        fixture = new AnalysisFixture
        {
            ExternalRoster =
            [
                new ExternalPlayerRecord("00-ROSTERED", "Rostered Player", "ABC", "RB", "00-ROSTERED", DateTimeOffset.UtcNow),
                new ExternalPlayerRecord("00-FREE", "Free Player", "ABC", "WR", "00-FREE", DateTimeOffset.UtcNow)
            ],
            ExternalIds = new Dictionary<string, string>
            {
                ["rostered"] = "00-ROSTERED",
                ["free"] = "00-FREE"
            }
        };
        trigger = new CapturingTrigger();
        factory = new ApiFactory(postgres.GetConnectionString(), fixture, trigger);
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        factory.Dispose();
        await postgres.DisposeAsync();
    }

    [Fact]
    public async Task Starts_analysis_executes_the_real_engine_and_is_idempotent()
    {
        await AuthenticateAndConfigureAsync();

        var token = await GetAntiforgeryTokenAsync();
        using var start = new HttpRequestMessage(HttpMethod.Post, "/api/v1/analysis/run");
        start.Headers.Add("RequestVerificationToken", token);
        var startResponse = await client.SendAsync(start);
        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);

        var queued = await startResponse.Content.ReadFromJsonAsync<AnalysisRunResponse>();
        Assert.NotNull(queued);
        Assert.Equal("fixture-operation", queued!.CloudRunExecutionId);
        Assert.Equal(queued.Id, Assert.Single(trigger.RunIds));

        async Task ExecuteAsync(string executionId)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<IAnalysisRunExecutor>()
                .ExecuteAsync(queued.Id, executionId, CancellationToken.None);
        }

        await Task.WhenAll(
            ExecuteAsync("fixture-execution"),
            ExecuteAsync("racing-execution"));

        var completed = await client.GetFromJsonAsync<AnalysisLatestResponse>(
            "/api/v1/analysis/latest");
        Assert.NotNull(completed);
        Assert.Equal("completed", completed!.Run.Status);
        Assert.NotNull(completed.Result);
        Assert.NotNull(completed.Run.CloudRunExecutionId);
        Assert.Equal("analysis-engine", completed.Run.Provider);

        using (var document = JsonDocument.Parse(completed.Result!.Value.GetRawText()))
        {
            Assert.Equal(
                "free",
                document.RootElement
                    .GetProperty("deterministic")
                    .GetProperty("waivers")
                    .GetProperty("recommendations")[0]
                    .GetProperty("playerId")
                    .GetString());
        }

        int evidenceCount;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            evidenceCount = await db.AnalysisEvidence.CountAsync(
                x => x.AnalysisRunId == queued.Id);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<IAnalysisRunExecutor>()
                .ExecuteAsync(queued.Id, "repeated-execution", CancellationToken.None);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Single(await db.AnalysisResults
                .Where(x => x.AnalysisRunId == queued.Id)
                .ToListAsync());
            Assert.Equal(
                evidenceCount,
                await db.AnalysisEvidence.CountAsync(x => x.AnalysisRunId == queued.Id));
        }
    }

    private async Task AuthenticateAndConfigureAsync()
    {
        var token = await GetAntiforgeryTokenAsync();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "admin@example.invalid",
                password = "LocalDevelopmentPassword1!",
                rememberMe = false
            })
        };
        login.Headers.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(login)).StatusCode);

        token = await GetAntiforgeryTokenAsync();
        using var save = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/league/configuration")
        {
            Content = JsonContent.Create(new
            {
                username = "manager",
                season = 2026,
                leagueId = "league-1",
                rosterId = "roster-1",
                teamName = (string?)null
            })
        };
        save.Headers.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(save)).StatusCode);
    }

    private async Task<string> GetAntiforgeryTokenAsync()
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/api/auth/antiforgery");
        return response.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("The antiforgery endpoint returned no token.");
    }

    private sealed class ApiFactory(
        string connectionString,
        AnalysisFixture fixture,
        CapturingTrigger trigger) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["Database:ApplyMigrations"] = "true"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILeagueDataProvider>();
                services.RemoveAll<IPerformanceDataProvider>();
                services.RemoveAll<IInjuryDataProvider>();
                services.RemoveAll<ILanguageModelProvider>();
                services.RemoveAll<IAnalysisExecutionTrigger>();
                services.AddSingleton<ILeagueDataProvider>(new StubLeagueProvider(fixture));
                services.AddSingleton<IPerformanceDataProvider>(new StubPerformanceProvider(fixture));
                services.AddSingleton<IInjuryDataProvider>(new StubInjuryProvider());
                services.AddSingleton<ILanguageModelProvider>(
                    new StubLanguageModelProvider
                    {
                        Response = new LanguageModelResponse(
                            "fixture-model",
                            "Fixture explanation",
                            [],
                            [],
                            DateTimeOffset.UtcNow)
                    });
                services.AddSingleton<IAnalysisExecutionTrigger>(trigger);
            });
        }
    }

    private sealed class CapturingTrigger : IAnalysisExecutionTrigger
    {
        public List<Guid> RunIds { get; } = [];

        public Task<string?> TriggerAsync(Guid analysisRunId, CancellationToken cancellationToken)
        {
            RunIds.Add(analysisRunId);
            return Task.FromResult<string?>("fixture-operation");
        }
    }

    private sealed record AnalysisRunResponse(
        Guid Id,
        string Status,
        int Attempt,
        string? Stage,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? LastHeartbeatAt,
        string? CloudRunExecutionId,
        string? Provider,
        string? ErrorCode,
        string? ErrorMessage,
        bool RetryAvailable,
        Guid? RetryOfRunId);

    private sealed record AnalysisLatestResponse(
        AnalysisRunResponse Run,
        JsonElement? Result);
}
