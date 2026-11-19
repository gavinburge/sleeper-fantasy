using System.ComponentModel.DataAnnotations;

namespace SleeperFantasy.Infrastructure.Configuration;

public sealed class RuntimeOptions
{
    public const string SectionName = "Runtime";

    [Required, Url]
    public string PublicBaseUrl { get; set; } = string.Empty;

    public string? CloudRunJobName { get; set; }
}
