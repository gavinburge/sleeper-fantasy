namespace SleeperFantasy.Domain.Providers;

public enum ProviderAvailability
{
    Available,
    Empty,
    RateLimited,
    Unavailable,
    Invalid
}

public sealed record ProviderResult<T>(
    ProviderAvailability Availability,
    T? Value,
    string Provider,
    DateTimeOffset ObservedAt,
    DateTimeOffset? FreshUntil = null,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool IsSuccessful => Availability is ProviderAvailability.Available or ProviderAvailability.Empty;

    public static ProviderResult<T> AvailableResult(
        T value,
        string provider,
        DateTimeOffset observedAt,
        DateTimeOffset? freshUntil = null) =>
        new(ProviderAvailability.Available, value, provider, observedAt, freshUntil);

    public static ProviderResult<T> EmptyResult(
        string provider,
        DateTimeOffset observedAt,
        DateTimeOffset? freshUntil = null) =>
        new(ProviderAvailability.Empty, default, provider, observedAt, freshUntil);

    public static ProviderResult<T> Failure(
        ProviderAvailability availability,
        string provider,
        DateTimeOffset observedAt,
        string errorCode,
        string errorMessage) =>
        new(availability, default, provider, observedAt, null, errorCode, errorMessage);
}
