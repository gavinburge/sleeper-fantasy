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

        var evidenceJson = JsonSerializer.Serialize(request.Evidence.Take(100));
        var prompt = $"{request.SystemInstruction}\n\nEvidence:\n{evidenceJson}\n\n{request.UserPrompt}";
        if (prompt.Length > settings.LanguageModelMaxPromptCharacters)
        {
            prompt = prompt[..settings.LanguageModelMaxPromptCharacters];
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.LanguageModelTimeoutSeconds));
        try
        {
            using var geminiRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"models/{Uri.EscapeDataString(settings.GeminiModel)}:generateContent")
            {
                Content = JsonContent.Create(new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[] { new { text = prompt } }
                        }
                    },
                    generationConfig = new
                    {
                        responseMimeType = "application/json",
                        temperature = 0.1
                    }
                })
            };
            geminiRequest.Headers.TryAddWithoutValidation("x-goog-api-key", settings.GeminiApiKey);
            using var response = await httpClient.SendAsync(geminiRequest, timeout.Token);

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
                await response.Content.ReadAsStreamAsync(timeout.Token),
                cancellationToken: timeout.Token);
            var text = GetResponseText(document.RootElement);
            if (string.IsNullOrWhiteSpace(text))
            {
                return InvalidResponse(observedAt, "Gemini returned no structured text.");
            }

            var parsed = JsonSerializer.Deserialize<LanguageModelResponseDto>(
                text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed is null)
            {
                return InvalidResponse(observedAt, "Gemini returned an empty structured response.");
            }

            if (parsed.Findings is null || parsed.Conflicts is null)
            {
                return InvalidResponse(observedAt, "Gemini returned an incomplete structured response.");
            }

            var knownEvidence = request.Evidence.Select(x => x.EvidenceId).ToHashSet(StringComparer.Ordinal);
            var findings = parsed.Findings
                .Where(x => !string.IsNullOrWhiteSpace(x.SubjectKey) && !string.IsNullOrWhiteSpace(x.Explanation))
                .Select(x => new LanguageModelFinding(
                    x.SubjectKey,
                    x.Explanation,
                    Math.Clamp(x.Confidence, 0m, 1m),
                    x.EvidenceIds.Where(knownEvidence.Contains).Distinct(StringComparer.Ordinal).ToArray(),
                    x.Caveats))
                .ToArray();
            if (findings.Length != parsed.Findings.Count)
            {
                return InvalidResponse(observedAt, "Gemini returned findings that did not satisfy the response contract.");
            }

            return ProviderResult<LanguageModelResponse>.AvailableResult(
                new LanguageModelResponse(
                    settings.GeminiModel,
                    parsed.Summary,
                    findings,
                    parsed.Conflicts,
                    DateTimeOffset.UtcNow),
                Name,
                observedAt,
                observedAt.AddMinutes(30));
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

    private sealed class LanguageModelResponseDto
    {
        public string? Summary { get; set; }
        public List<FindingDto>? Findings { get; set; }
        public List<string>? Conflicts { get; set; }
    }

    private sealed class FindingDto
    {
        public string SubjectKey { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public List<string> EvidenceIds { get; set; } = [];
        public List<string> Caveats { get; set; } = [];
    }
}
