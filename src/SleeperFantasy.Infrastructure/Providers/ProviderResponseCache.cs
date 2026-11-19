using System.Collections.Concurrent;
using System.Text.Json;

namespace SleeperFantasy.Infrastructure.Providers;

public interface IProviderResponseCache
{
    bool TryGet(string providerKey, string resourceKey, out ProviderResponseCacheEntry entry);
    void Set(string providerKey, string resourceKey, ProviderResponseCacheEntry entry, int maxEntries);
    void Remove(string providerKey, string resourceKey);
    SemaphoreSlim GetRequestGate(string providerKey);
}

public sealed record ProviderResponseCacheEntry(
    JsonDocument Value,
    DateTimeOffset ObservedAt,
    DateTimeOffset ExpiresAt);

public sealed class ProviderResponseCache : IProviderResponseCache
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ProviderResponseCacheEntry>> entries = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> requestGates = new();

    public bool TryGet(string providerKey, string resourceKey, out ProviderResponseCacheEntry entry)
    {
        entry = null!;
        if (!entries.TryGetValue(providerKey, out var providerEntries)
            || !providerEntries.TryGetValue(resourceKey, out var found))
        {
            entry = null!;
            return false;
        }
        entry = found;

        if (entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return true;
        }

        providerEntries.TryRemove(resourceKey, out _);
        entry = null!;
        return false;
    }

    public void Remove(string providerKey, string resourceKey)
    {
        if (entries.TryGetValue(providerKey, out var providerEntries))
        {
            providerEntries.TryRemove(resourceKey, out _);
        }
    }

    public void Set(string providerKey, string resourceKey, ProviderResponseCacheEntry entry, int maxEntries)
    {
        var providerEntries = entries.GetOrAdd(providerKey, _ => new());
        providerEntries[resourceKey] = entry;
        while (providerEntries.Count > maxEntries
               && providerEntries.Keys.FirstOrDefault() is { } oldest)
        {
            providerEntries.TryRemove(oldest, out _);
        }
    }

    public SemaphoreSlim GetRequestGate(string providerKey) =>
        requestGates.GetOrAdd(providerKey, _ => new SemaphoreSlim(1, 1));
}
