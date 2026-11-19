using System.ComponentModel.DataAnnotations;

namespace SleeperFantasy.Infrastructure.Configuration;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    [Range(1, 168)]
    public int DefaultTtlHours { get; set; } = 24;

    [Range(1, 1000)]
    public int MaxEntriesPerProvider { get; set; } = 100;
}
