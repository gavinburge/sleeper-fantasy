using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SleeperFantasy.Application.Abstractions;
using SleeperFantasy.Application.Features.Authentication;
using SleeperFantasy.Application.Features.Analysis;
using SleeperFantasy.Application.Features.LeagueSetup;
using SleeperFantasy.Application.Providers;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Infrastructure.Configuration;
using SleeperFantasy.Infrastructure.Identity;
using SleeperFantasy.Infrastructure.Persistence;
using SleeperFantasy.Infrastructure.Providers;

namespace SleeperFantasy.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetSection(DatabaseOptions.SectionName)["ConnectionString"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A PostgreSQL connection string is required in ConnectionStrings:DefaultConnection.");
        }

        services.AddOptions<DatabaseOptions>()
            .Configure(options =>
            {
                options.ConnectionString = connectionString;
                options.ApplyMigrations = configuration.GetValue<bool>(
                    $"{DatabaseOptions.SectionName}:ApplyMigrations");
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ProviderOptions>()
            .Bind(configuration.GetSection(ProviderOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options =>
                !string.Equals(options.LanguageModel, "Gemini", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(options.GeminiApiKey),
                "Providers:GeminiApiKey is required when Providers:LanguageModel is Gemini.")
            .ValidateOnStart();
        services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<RuntimeOptions>()
            .Bind(configuration.GetSection(RuntimeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<Configuration.IdentityOptions>()
            .Bind(configuration.GetSection(Configuration.IdentityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddHttpContextAccessor();
        services.AddScoped<IAdministratorSession, IdentityAdministratorSession>();
        services.AddScoped<LeagueConfigurationService>();
        services.AddScoped<SourceRefreshService>();
        services.AddScoped<RosterRiskAnalyzer>();
        services.AddScoped<AvailablePlayerService>();
        services.AddScoped<WaiverRankingService>();
        services.AddScoped<LanguageModelAnalysisService>();
        services.AddScoped<AnalysisOrchestrator>();
        services.AddScoped<AnalysisPersistenceService>();
        services.AddScoped<AnalysisRunService>();
        services.AddScoped<AnalysisRunExecutor>();
        services.AddScoped<IAnalysisRunExecutor>(sp => sp.GetRequiredService<AnalysisRunExecutor>());
        services.AddScoped<IAnalysisExecutionTrigger, CloudRunAnalysisExecutionTrigger>();
        services.AddSingleton<IPlayerIdentityResolver, PlayerIdentityResolver>();
        services.AddSingleton<IProviderResponseCache, ProviderResponseCache>();

        services.AddHttpClient<ILeagueDataProvider, Providers.SleeperDataProvider>((sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ProviderOptions>>().Value.SleeperBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient<IPerformanceDataProvider, Providers.NflverseDataProvider>((sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ProviderOptions>>().Value.NflverseBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<IInjuryDataProvider, Providers.NflInjuryReportProvider>((sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ProviderOptions>>().Value.InjuryReportBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient<ILanguageModelProvider, Providers.GeminiLanguageModelProvider>((sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ProviderOptions>>().Value.GeminiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<ProviderOptions>>().Value.LanguageModelTimeoutSeconds);
        });

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = ".SleeperFantasy.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = configuration.GetValue("Identity:RequireHttps", true)
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        return services;
    }
}
