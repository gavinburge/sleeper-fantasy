using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Infrastructure.Configuration;

namespace SleeperFantasy.Infrastructure.Providers;

public abstract class ProviderHttpClient(
    HttpClient httpClient,
    IOptions<CacheOptions> cacheOptions,
    ILogger logger)
{
    protected Uri? BaseAddress => httpClient.BaseAddress;

    private readonly SemaphoreSlim requestGate = new(1, 1);
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new();
    private readonly TimeSpan minimumRequestInterval = TimeSpan.FromMilliseconds(250);
    private DateTimeOffset lastRequestAt = DateTimeOffset.MinValue;

    protected async Task<ProviderResponse<T>> GetJsonAsync<T>(
        string resourceKey,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(resourceKey, out var cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return new ProviderResponse<T>(
                cached.Value.Deserialize<T>(),
                ProviderAvailability.Available,
                cached.ObservedAt,
                cached.ExpiresAt);
        }

        await requestGate.WaitAsync(cancellationToken);
        try
        {
            var wait = minimumRequestInterval - (DateTimeOffset.UtcNow - lastRequestAt);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            using var response = await httpClient.GetAsync(resourceKey, cancellationToken);
            lastRequestAt = DateTimeOffset.UtcNow;
            var observedAt = DateTimeOffset.UtcNow;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new ProviderResponse<T>(default, ProviderAvailability.RateLimited, observedAt, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Provider request failed with status {StatusCode} for {ResourceKey}.",
                    (int)response.StatusCode,
                    resourceKey);
                return new ProviderResponse<T>(default, ProviderAvailability.Unavailable, observedAt, null);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            if (value is null)
            {
                return new ProviderResponse<T>(default, ProviderAvailability.Invalid, observedAt, null);
            }

            var freshUntil = observedAt.AddHours(cacheOptions.Value.DefaultTtlHours);
            var payload = JsonSerializer.SerializeToDocument(value);
            cache[resourceKey] = new CacheEntry(payload, observedAt, freshUntil);
            while (cache.Count > cacheOptions.Value.MaxEntriesPerProvider
                   && cache.TryGetValue(cache.Keys.First(), out _))
            {
                cache.TryRemove(cache.Keys.First(), out _);
            }

            return new ProviderResponse<T>(value, ProviderAvailability.Available, observedAt, freshUntil);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProviderResponse<T>(
                default,
                ProviderAvailability.Unavailable,
                DateTimeOffset.UtcNow,
                null);
        }
        catch (HttpRequestException)
        {
            return new ProviderResponse<T>(
                default,
                ProviderAvailability.Unavailable,
                DateTimeOffset.UtcNow,
                null);
        }
        finally
        {
            requestGate.Release();
        }
    }

    protected async Task<ProviderResponse<string>> GetTextAsync(
        string resourceKey,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"text:{resourceKey}";
        if (cache.TryGetValue(cacheKey, out var cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return new ProviderResponse<string>(
                cached.Value.Deserialize<string>(),
                ProviderAvailability.Available,
                cached.ObservedAt,
                cached.ExpiresAt);
        }

        await requestGate.WaitAsync(cancellationToken);
        try
        {
            var wait = minimumRequestInterval - (DateTimeOffset.UtcNow - lastRequestAt);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            using var response = await httpClient.GetAsync(resourceKey, cancellationToken);
            lastRequestAt = DateTimeOffset.UtcNow;
            var observedAt = DateTimeOffset.UtcNow;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new ProviderResponse<string>(default, ProviderAvailability.RateLimited, observedAt, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Provider request failed with status {StatusCode} for {ResourceKey}.",
                    (int)response.StatusCode,
                    resourceKey);
                return new ProviderResponse<string>(default, ProviderAvailability.Unavailable, observedAt, null);
            }

            var value = await response.Content.ReadAsStringAsync(cancellationToken);
            var availability = string.IsNullOrWhiteSpace(value)
                ? ProviderAvailability.Empty
                : ProviderAvailability.Available;
            var freshUntil = observedAt.AddHours(cacheOptions.Value.DefaultTtlHours);
            var payload = JsonSerializer.SerializeToDocument(value);
            cache[cacheKey] = new CacheEntry(payload, observedAt, freshUntil);
            while (cache.Count > cacheOptions.Value.MaxEntriesPerProvider
                   && cache.TryGetValue(cache.Keys.First(), out _))
            {
                cache.TryRemove(cache.Keys.First(), out _);
            }

            return new ProviderResponse<string>(value, availability, observedAt, freshUntil);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProviderResponse<string>(
                default,
                ProviderAvailability.Unavailable,
                DateTimeOffset.UtcNow,
                null);
        }
        catch (HttpRequestException)
        {
            return new ProviderResponse<string>(
                default,
                ProviderAvailability.Unavailable,
                DateTimeOffset.UtcNow,
                null);
        }
        finally
        {
            requestGate.Release();
        }
    }

    protected static ProviderResult<T> ToResult<T>(
        ProviderResponse<T> response,
        string provider,
        Func<T, bool>? isEmpty = null)
    {
        if (response.Availability != ProviderAvailability.Available)
        {
            return ProviderResult<T>.Failure(
                response.Availability,
                provider,
                response.ObservedAt,
                response.Availability.ToString().ToUpperInvariant(),
                $"The {provider} provider returned {response.Availability.ToString().ToLowerInvariant()} data.");
        }

        if (response.Value is null || isEmpty?.Invoke(response.Value) == true)
        {
            return ProviderResult<T>.EmptyResult(provider, response.ObservedAt, response.FreshUntil);
        }

        return ProviderResult<T>.AvailableResult(
            response.Value,
            provider,
            response.ObservedAt,
            response.FreshUntil);
    }

    private sealed record CacheEntry(
        JsonDocument Value,
        DateTimeOffset ObservedAt,
        DateTimeOffset ExpiresAt);

    protected sealed record ProviderResponse<T>(
        T? Value,
        ProviderAvailability Availability,
        DateTimeOffset ObservedAt,
        DateTimeOffset? FreshUntil);
}
