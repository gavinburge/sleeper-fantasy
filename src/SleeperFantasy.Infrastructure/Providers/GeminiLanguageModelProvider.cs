using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SleeperFantasy.Domain.Providers;
using SleeperFantasy.Infrastructure.Configuration;

namespace SleeperFantasy.Infrastructure.Providers;

public sealed class GeminiLanguageModelProvider(
    HttpClient httpClient,
    IOptions<ProviderOptions> options,
    ILogger<GeminiLanguageModelProvider> logger)
    : ILanguageModelProvider
{
    private readonly ProviderOptions settings = options.Value;

    public const string Name = "Gemini";
    public string ProviderName => Name;

    public async Task<ProviderResult<LanguageModelResponse>> AnalyzeAsync(
        LanguageModelRequest request,
        CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(settings.GeminiApiKey))
        {
            return ProviderResult<LanguageModelResponse>.Failure(
                ProviderAvailability.Invalid,
                Name,
                observedAt,
                "MISSING_API_KEY",
                "Gemini API key is not configured.");
        }

        var prompt = BuildPrompt(request, settings.LanguageModelMaxPromptCharacters);

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptTimeout.CancelAfter(TimeSpan.FromSeconds(settings.LanguageModelTimeoutSeconds));
                using var geminiRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"models/{Uri.EscapeDataString(settings.GeminiModel)}:generateContent")
                {
                    Content = JsonContent.Create(new
                    {
                        contents = new[] { new { parts = new[] { new { text = prompt } } } },
                        generationConfig = new { responseMimeType = "application/json", temperature = 0.1 }
                    })
                };
                geminiRequest.Headers.TryAddWithoutValidation("x-goog-api-key", settings.GeminiApiKey);

                HttpResponseMessage response;
                try
                {
                    response = await httpClient.SendAsync(geminiRequest, attemptTimeout.Token);
                }
                catch (HttpRequestException) when (attempt < settings.LanguageModelMaxRetries)
                {
                    await DelayBeforeRetry(attempt, cancellationToken);
                    continue;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < settings.LanguageModelMaxRetries)
                {
                    await DelayBeforeRetry(attempt, cancellationToken);
                    continue;
                }

                using (response)
                {
                    var retryable = response.StatusCode == HttpStatusCode.TooManyRequests
                        || (int)response.StatusCode >= 500;
                    if (retryable && attempt < settings.LanguageModelMaxRetries)
                    {
                        await DelayBeforeRetry(attempt, cancellationToken);
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        return ProviderResult<LanguageModelResponse>.Failure(
                            ProviderAvailability.RateLimited, Name, observedAt, "RATE_LIMITED", "Gemini rate limit reached.");
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogWarning("Gemini request failed with status {StatusCode}.", (int)response.StatusCode);
                        return ProviderResult<LanguageModelResponse>.Failure(
                            ProviderAvailability.Unavailable, Name, observedAt, "MODEL_UNAVAILABLE", "Gemini did not return a successful response.");
                    }

                    using var document = await JsonDocument.ParseAsync(
                        await response.Content.ReadAsStreamAsync(attemptTimeout.Token), cancellationToken: attemptTimeout.Token);
                    var text = GetResponseText(document.RootElement);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return InvalidResponse(observedAt, "Gemini returned no structured text.");
                    }

                    LanguageModelResponseDto? parsed;
                    try
                    {
                        parsed = JsonSerializer.Deserialize<LanguageModelResponseDto>(
                            text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (JsonException exception)
                    {
                        return InvalidResponse(observedAt, $"Gemini returned invalid JSON: {exception.Message}");
                    }
                    if (parsed is null)
                    {
                        return InvalidResponse(observedAt, "Gemini returned an empty structured response.");
                    }

                    if (!HasRequiredResponseProperties(text, out var validationError))
                    {
                        return InvalidResponse(observedAt, validationError);
                    }

                    var knownEvidence = request.Evidence.Select(x => x.EvidenceId).ToHashSet(StringComparer.Ordinal);
                    var findings = parsed.Findings!
                        .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.SubjectKey) && !string.IsNullOrWhiteSpace(x.Explanation))
                        .Select(x => new LanguageModelFinding(
                            x!.SubjectKey!, x.Explanation!, Math.Clamp(x.Confidence ?? 0m, 0m, 1m),
                            (x.EvidenceIds ?? []).Where(knownEvidence.Contains).Distinct(StringComparer.Ordinal).ToArray(),
                            x.Caveats ?? []))
                        .ToArray();

                    return ProviderResult<LanguageModelResponse>.AvailableResult(
                        new LanguageModelResponse(settings.GeminiModel, parsed.Summary, findings,
                            parsed.Conflicts!, DateTimeOffset.UtcNow),
                        Name, observedAt, observedAt.AddMinutes(30));
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult<LanguageModelResponse>.Failure(
                ProviderAvailability.Unavailable, Name, observedAt, "TIMEOUT", "Gemini request timed out.");
        }
        catch (JsonException exception)
        {
            return InvalidResponse(observedAt, $"Gemini returned invalid JSON: {exception.Message}");
        }
        catch (HttpRequestException)
        {
            return ProviderResult<LanguageModelResponse>.Failure(
                ProviderAvailability.Unavailable, Name, observedAt, "MODEL_UNAVAILABLE", "Gemini request failed.");
        }
    }

    private static ProviderResult<LanguageModelResponse> InvalidResponse(
        DateTimeOffset observedAt,
        string message) =>
        ProviderResult<LanguageModelResponse>.Failure(
            ProviderAvailability.Invalid,
            Name,
            observedAt,
            "INVALID_MODEL_RESPONSE",
            message);

    private static string? GetResponseText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array
            || parts.GetArrayLength() == 0
            || !parts[0].TryGetProperty("text", out var text)
            || text.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return text.GetString();
    }

    private static bool HasRequiredResponseProperties(
        string text,
        out string error)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "Gemini structured response was not a JSON object.";
            return false;
        }

        foreach (var propertyName in new[] { "findings", "conflicts" })
        {
            if (!root.TryGetProperty(propertyName, out var property)
                && !root.TryGetProperty(ToPascalCase(propertyName), out property))
            {
                error = $"Gemini structured response is missing required property '{propertyName}'.";
                return false;
            }

            if (property.ValueKind != JsonValueKind.Array)
            {
                error = $"Gemini structured response property '{propertyName}' must be an array.";
                return false;
            }
        }

        if (root.TryGetProperty("summary", out var summary)
            || root.TryGetProperty("Summary", out summary))
        {
            if (summary.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                error = "Gemini structured response property 'summary' must be a string or null.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static string ToPascalCase(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];

    private async Task DelayBeforeRetry(int attempt, CancellationToken cancellationToken)
    {
        var delay = Math.Max(0, settings.LanguageModelRetryDelayMilliseconds) * (attempt + 1);
        await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
    }

    private static string BuildPrompt(LanguageModelRequest request, int maxCharacters)
    {
        var limit = Math.Max(256, maxCharacters);
        var evidence = request.Evidence.Take(100).Select(x => new PromptEvidence(
            x.EvidenceId, x.SubjectKey, x.Fact, x.Source, x.ObservedAt, x.FreshUntil, x.Confidence)).ToList();
        string Serialize() => JsonSerializer.Serialize(evidence);
        var system = request.SystemInstruction;
        var user = request.UserPrompt;
        var deterministic = request.DeterministicSummary;
        string Compose() =>
            $"{system}\n\nDeterministic summary (authoritative; do not override):\n{deterministic}\n\nEvidence:\n{Serialize()}\n\n{user}";

        while (evidence.Count > 0 && Compose().Length > limit)
            evidence.RemoveAt(evidence.Count - 1);
        while (Compose().Length > limit && deterministic.Length > 0)
            deterministic = deterministic[..(deterministic.Length / 2)];
        while (Compose().Length > limit && evidence.Count > 0)
            evidence.RemoveAt(evidence.Count - 1);
        while (Compose().Length > limit && user.Length > 0)
            user = user[..(user.Length / 2)];
        while (Compose().Length > limit && system.Length > 0)
            system = system[..(system.Length / 2)];

        var prompt = Compose();
        return prompt[..Math.Min(prompt.Length, limit)];
    }

    private sealed record PromptEvidence(
        string EvidenceId,
        string SubjectKey,
        string Fact,
        string Source,
        DateTimeOffset ObservedAt,
        DateTimeOffset? FreshUntil,
        decimal? Confidence);

    private sealed class LanguageModelResponseDto
    {
        public string? Summary { get; set; }
        public List<FindingDto> Findings { get; set; } = [];
        public List<string> Conflicts { get; set; } = [];
    }

    private sealed class FindingDto
    {
        public string SubjectKey { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public decimal? Confidence { get; set; }
        public List<string>? EvidenceIds { get; set; }
        public List<string>? Caveats { get; set; }
    }
}
