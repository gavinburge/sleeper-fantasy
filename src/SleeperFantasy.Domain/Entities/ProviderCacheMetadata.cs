namespace SleeperFantasy.Domain.Entities;

public sealed class ProviderCacheMetadata
{
    public Guid Id { get; set; }
    public required string Provider { get; set; }
    public required string ResourceKey { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? ETag { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
