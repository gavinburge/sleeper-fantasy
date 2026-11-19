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
    public string GeminiModel { get; set; } = "gemini-2.0-flash";
    public string GeminiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    [Range(1, 10080)]
    public int GeminiResultTtlMinutes { get; set; } = 30;
    public int LanguageModelTimeoutSeconds { get; set; } = 30;
    public int LanguageModelMaxPromptCharacters { get; set; } = 24000;
    public int LanguageModelMaxRetries { get; set; } = 2;
    public int LanguageModelRetryDelayMilliseconds { get; set; } = 250;
}
