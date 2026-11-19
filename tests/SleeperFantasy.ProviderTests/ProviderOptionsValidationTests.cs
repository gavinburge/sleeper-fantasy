using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SleeperFantasy.Infrastructure;
using SleeperFantasy.Infrastructure.Configuration;
using Xunit;

namespace SleeperFantasy.ProviderTests;

public sealed class ProviderOptionsValidationTests
{
    [Theory]
    [InlineData("Cache:SleeperTtlHours")]
    [InlineData("Cache:NflverseTtlHours")]
    [InlineData("Cache:InjuryReportTtlHours")]
    public void Non_positive_source_ttl_is_rejected(string key)
    {
        var provider = BuildServiceProvider(key, "0");

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CacheOptions>>().Value);
    }

    [Fact]
    public void Non_positive_gemini_ttl_is_rejected()
    {
        var provider = BuildServiceProvider("Providers:GeminiResultTtlMinutes", "0");

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ProviderOptions>>().Value);
    }

    private static ServiceProvider BuildServiceProvider(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["Providers:SleeperBaseUrl"] = "https://sleeper.test",
                ["Providers:NflverseBaseUrl"] = "https://nflverse.test",
                ["Providers:InjuryReportBaseUrl"] = "https://injury.test",
                ["Providers:GeminiBaseUrl"] = "https://gemini.test/v1beta",
                ["Database:ApplyMigrations"] = "false",
                [key] = value
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
