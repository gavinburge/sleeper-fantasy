using System.ComponentModel.DataAnnotations;

namespace SleeperFantasy.Infrastructure.Configuration;

public sealed class ProviderOptions
{
    public const string SectionName = "Providers";

    [Required, Url]
    public string SleeperBaseUrl { get; set; } = string.Empty;

    [Required, Url]
    public string NflverseBaseUrl { get; set; } = string.Empty;

    [Required, Url]
    public string InjuryReportBaseUrl { get; set; } = string.Empty;

    public string LanguageModel { get; set; } = "None";
    public string? GeminiApiKey { get; set; }
}
