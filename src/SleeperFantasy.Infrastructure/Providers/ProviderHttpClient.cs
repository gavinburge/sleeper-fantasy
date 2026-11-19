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
    ILogger logger,
    IProviderResponseCache? sharedCache = null)
{
    protected Uri? BaseAddress => httpClient.BaseAddress;
    protected CacheOptions CacheSettings => cacheOptions.Value;
    protected virtual TimeSpan CacheTtl => TimeSpan.FromHours(cacheOptions.Value.DefaultTtlHours);
    protected void RemoveCachedText(string resourceKey) =>
        responseCache.Remove(ProviderKey, $"text:{resourceKey}");

    private readonly IProviderResponseCache responseCache = sharedCache ?? new ProviderResponseCache();
    private string ProviderKey => $"{GetType().FullName}:{httpClient.BaseAddress}";
    private readonly TimeSpan minimumRequestInterval = TimeSpan.FromMilliseconds(250);
    private DateTimeOffset lastRequestAt = DateTimeOffset.MinValue;

    protected async Task<ProviderResponse<T>> GetJsonAsync<T>(
        string resourceKey,
        CancellationToken cancellationToken)
    {
        if (responseCache.TryGet(ProviderKey, resourceKey, out var cached))
        {
            try
            {
                return new ProviderResponse<T>(
                    cached.Value.Deserialize<T>(),
                    ProviderAvailability.Available,
                    cached.ObservedAt,
                    cached.ExpiresAt);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Cached provider response was invalid for {ResourceKey}.", resourceKey);
                return InvalidJsonResponse<T>(cached.ObservedAt);
            }
        }

        var requestGate = responseCache.GetRequestGate(ProviderKey);
        await requestGate.WaitAsync(cancellationToken);
        try
        {
            if (responseCache.TryGet(ProviderKey, resourceKey, out cached))
            {
                try
                {
                    return new ProviderResponse<T>(
                        cached.Value.Deserialize<T>(),
                        ProviderAvailability.Available,
                        cached.ObservedAt,
                        cached.ExpiresAt);
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
                {
                    logger.LogWarning(exception, "Cached provider response was invalid for {ResourceKey}.", resourceKey);
                    return InvalidJsonResponse<T>(cached.ObservedAt);
                }
            }

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

            var freshUntil = observedAt.Add(CacheTtl);
            var payload = JsonSerializer.SerializeToDocument(value);
            responseCache.Set(
                ProviderKey,
                resourceKey,
                new ProviderResponseCacheEntry(payload, observedAt, freshUntil),
                cacheOptions.Value.MaxEntriesPerProvider);

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
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Provider response contained invalid JSON for {ResourceKey}.", resourceKey);
            return InvalidJsonResponse<T>(DateTimeOffset.UtcNow);
        }
        catch (NotSupportedException exception)
        {
            logger.LogWarning(exception, "Provider response schema was unsupported for {ResourceKey}.", resourceKey);
            return InvalidJsonResponse<T>(DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Provider response schema was invalid for {ResourceKey}.", resourceKey);
            return InvalidJsonResponse<T>(DateTimeOffset.UtcNow);
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
        if (responseCache.TryGet(ProviderKey, cacheKey, out var cached))
        {
            try
            {
                return new ProviderResponse<string>(
                    cached.Value.Deserialize<string>(),
                    ProviderAvailability.Available,
                    cached.ObservedAt,
                    cached.ExpiresAt);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Cached provider response was invalid for {ResourceKey}.", resourceKey);
                return InvalidJsonResponse<string>(cached.ObservedAt);
            }
        }

        var requestGate = responseCache.GetRequestGate(ProviderKey);
        await requestGate.WaitAsync(cancellationToken);
        try
        {
            if (responseCache.TryGet(ProviderKey, cacheKey, out cached))
            {
                try
                {
                    return new ProviderResponse<string>(
                        cached.Value.Deserialize<string>(),
                        ProviderAvailability.Available,
                        cached.ObservedAt,
                        cached.ExpiresAt);
                }
                catch (JsonException exception)
                {
                    logger.LogWarning(exception, "Cached provider response was invalid for {ResourceKey}.", resourceKey);
                    return InvalidJsonResponse<string>(cached.ObservedAt);
                }
            }

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
            var freshUntil = observedAt.Add(CacheTtl);
            var payload = JsonSerializer.SerializeToDocument(value);
            responseCache.Set(
                ProviderKey,
                cacheKey,
                new ProviderResponseCacheEntry(payload, observedAt, freshUntil),
                cacheOptions.Value.MaxEntriesPerProvider);

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
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Provider response contained invalid JSON for {ResourceKey}.", resourceKey);
            return InvalidJsonResponse<string>(DateTimeOffset.UtcNow);
        }
        catch (NotSupportedException exception)
        {
            logger.LogWarning(exception, "Provider response schema was unsupported for {ResourceKey}.", resourceKey);
            return InvalidJsonResponse<string>(DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Provider response schema was invalid for {ResourceKey}.", resourceKey);
            return InvalidJsonResponse<string>(DateTimeOffset.UtcNow);
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

    protected sealed record ProviderResponse<T>(
        T? Value,
        ProviderAvailability Availability,
        DateTimeOffset ObservedAt,
        DateTimeOffset? FreshUntil);

    private static ProviderResponse<T> InvalidJsonResponse<T>(DateTimeOffset observedAt) =>
        new(default, ProviderAvailability.Invalid, observedAt, null);
}
