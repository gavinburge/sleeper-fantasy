using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SleeperFantasy.Domain.Providers;
using Testcontainers.PostgreSql;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class LeagueApiEndToEndTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17")
        .Build();

    private ApiFactory? factory;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        factory = new ApiFactory(postgres.GetConnectionString());
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        factory?.Dispose();
        await postgres.DisposeAsync();
    }

    [Fact]
    public async Task Manager_can_authenticate_discover_save_and_read_configuration()
    {
        var antiforgeryToken = await GetAntiforgeryTokenAsync();

        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "admin@example.invalid",
                password = "LocalDevelopmentPassword1!",
                rememberMe = false
            })
        };
        login.Headers.Add("RequestVerificationToken", antiforgeryToken);

        var loginResponse = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var session = await client.GetFromJsonAsync<SessionResponse>("/api/auth/session");
        Assert.True(session?.Authenticated);

        var discoveryResponse = await client.GetAsync(
            "/api/v1/league/discovery?username=fixture-manager&season=2026");
        Assert.Equal(HttpStatusCode.OK, discoveryResponse.StatusCode);
        var discovery = await discoveryResponse.Content.ReadFromJsonAsync<LeagueDiscoveryResponse>();
        Assert.NotNull(discovery);
        Assert.Equal("fixture-user", discovery!.User.UserId);
        Assert.Single(discovery.Leagues);
        Assert.Contains(discovery.Leagues[0].Teams, team => team.IsManagerTeam && team.RosterId == "8");

        antiforgeryToken = await GetAntiforgeryTokenAsync();
        using var save = new HttpRequestMessage(HttpMethod.Post, "/api/v1/league/configuration")
        {
            Content = JsonContent.Create(new
            {
                username = "fixture-manager",
                season = 2026,
                leagueId = "fixture-league",
                rosterId = "8",
                teamName = (string?)null
            })
        };
        save.Headers.Add("RequestVerificationToken", antiforgeryToken);

        var saveResponse = await client.SendAsync(save);
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

        var configurationResponse = await client.GetAsync("/api/v1/league");
        Assert.Equal(HttpStatusCode.OK, configurationResponse.StatusCode);
        var configuration =
            await configurationResponse.Content.ReadFromJsonAsync<LeagueConfigurationResponse>();
        Assert.NotNull(configuration);
        Assert.Equal("fixture-user", configuration!.SleeperUserId);
        Assert.Equal("fixture-league", configuration.LeagueId);
        Assert.Equal("8", configuration.RosterId);
        Assert.Equal(2026, configuration.Season);
    }

    [Fact]
    public async Task Second_client_request_reuses_the_same_queued_analysis_run()
    {
        var antiforgeryToken = await GetAntiforgeryTokenAsync();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "admin@example.invalid",
                password = "LocalDevelopmentPassword1!",
                rememberMe = false
            })
        };
        login.Headers.Add("RequestVerificationToken", antiforgeryToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(login)).StatusCode);

        antiforgeryToken = await GetAntiforgeryTokenAsync();
        using var save = new HttpRequestMessage(HttpMethod.Post, "/api/v1/league/configuration")
        {
            Content = JsonContent.Create(new
            {
                username = "fixture-manager",
                season = 2026,
                leagueId = "fixture-league",
                rosterId = "8",
                teamName = (string?)null
            })
        };
        save.Headers.Add("RequestVerificationToken", antiforgeryToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(save)).StatusCode);

        async Task<(HttpStatusCode Status, AnalysisRunResponse? Run)> StartAsync()
        {
            var token = await GetAntiforgeryTokenAsync();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/analysis/run");
            request.Headers.Add("RequestVerificationToken", token);
            var response = await client.SendAsync(request);
            return (
                response.StatusCode,
                await response.Content.ReadFromJsonAsync<AnalysisRunResponse>());
        }

        var responses = await Task.WhenAll(StartAsync(), StartAsync());
        var first = responses[0];
        var second = responses[1];

        Assert.Equal(HttpStatusCode.Accepted, first.Status);
        Assert.Equal(HttpStatusCode.Accepted, second.Status);
        Assert.NotNull(first.Run);
        Assert.Equal(first.Run!.Id, second.Run!.Id);

        var latest = await client.GetFromJsonAsync<AnalysisLatestResponse>("/api/v1/analysis/latest");
        Assert.NotNull(latest);
        Assert.Equal(first.Run.Id, latest!.Run.Id);
        Assert.Equal("queued", latest.Run.Status);
        Assert.Null(latest.Result);
    }

    private async Task<string> GetAntiforgeryTokenAsync()
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/api/auth/antiforgery");
        return response.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("The antiforgery endpoint returned no token.");
    }

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
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
                var providerRegistration = services.FirstOrDefault(
                    descriptor => descriptor.ServiceType == typeof(ILeagueDataProvider));
                if (providerRegistration is not null)
                {
                    services.Remove(providerRegistration);
                }

                services.AddSingleton<ILeagueDataProvider, FixtureLeagueProvider>();
            });
        }
    }

    private sealed class FixtureLeagueProvider : ILeagueDataProvider
    {
        public string ProviderName => "fixture";

        public Task<ProviderResult<SleeperUser>> GetUserAsync(
            string username,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<SleeperUser>.AvailableResult(
                new SleeperUser("fixture-user", username, "Fixture Manager"),
                ProviderName,
                DateTimeOffset.UtcNow));

        public Task<ProviderResult<IReadOnlyList<LeagueSummary>>> GetLeaguesAsync(
            string userId,
            string season,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<IReadOnlyList<LeagueSummary>>.AvailableResult(
                [new LeagueSummary("fixture-league", "Fixture League", season, "in_season", "nfl", 12, "ppr")],
                ProviderName,
                DateTimeOffset.UtcNow));

        public Task<ProviderResult<IReadOnlyList<RosterRecord>>> GetRostersAsync(
            string leagueId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<IReadOnlyList<RosterRecord>>.AvailableResult(
                [
                    new RosterRecord("8", "fixture-user", [], [], null, null, null, null, "Fixture Team"),
                    new RosterRecord("9", "other-user", [], [], null, null, null, null, "Other Team")
                ],
                ProviderName,
                DateTimeOffset.UtcNow));

        public Task<ProviderResult<LeagueSummary>> GetLeagueAsync(
            string leagueId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderResult<LeagueSettings>> GetSettingsAsync(
            string leagueId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderResult<IReadOnlyList<MatchupRecord>>> GetMatchupsAsync(
            string leagueId,
            int week,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderResult<IReadOnlyList<TransactionRecord>>> GetTransactionsAsync(
            string leagueId,
            int round,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderResult<IReadOnlyDictionary<string, PlayerRecord>>> GetPlayersAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderResult<LeagueState>> GetStateAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderResult<IReadOnlyList<TrendingPlayer>>> GetTrendingPlayersAsync(
            string sport,
            string type,
            int lookbackHours,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed record SessionResponse(bool Authenticated);

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

    private sealed record LeagueDiscoveryResponse(
        SleeperUser User,
        IReadOnlyList<LeagueDiscoveryLeague> Leagues);

    private sealed record LeagueDiscoveryLeague(
        LeagueSummary League,
        IReadOnlyList<LeagueTeam> Teams);

    private sealed record LeagueTeam(
        string RosterId,
        string OwnerUserId,
        string TeamName,
        bool IsManagerTeam);

    private sealed record LeagueConfigurationResponse(
        Guid Id,
        string SleeperUsername,
        string SleeperUserId,
        string LeagueId,
        string TeamName,
        string RosterId,
        int Season,
        DateTimeOffset ConfiguredAt);
}
