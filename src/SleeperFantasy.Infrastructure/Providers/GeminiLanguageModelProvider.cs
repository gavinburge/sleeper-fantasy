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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
            using (var response = await new GeminiResilienceExecutor(settings).ExecuteAsync(
                attemptCancellation =>
                {
                    var geminiRequest = new HttpRequestMessage(
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
                    return SendAndDisposeAsync(httpClient, geminiRequest, attemptCancellation);
                },
                cancellationToken))
            {
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        return ProviderResult<LanguageModelResponse>.Failure(
                            ProviderAvailability.RateLimited, Name, observedAt, "RATE_LIMITED", "Gemini rate limit reached.");
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogWarning("Gemini request failed with status {StatusCode}.", (int)response.StatusCode);
                        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        var errorCode = response.StatusCode switch
                        {
                            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "AUTHENTICATION_FAILED",
                            HttpStatusCode.NotFound => "MODEL_NOT_FOUND",
                            _ => "MODEL_UNAVAILABLE"
                        };
                        var detail = string.IsNullOrWhiteSpace(responseBody)
                            ? string.Empty
                            : $" Response: {responseBody[..Math.Min(responseBody.Length, 500)]}";
                        return ProviderResult<LanguageModelResponse>.Failure(
                            ProviderAvailability.Unavailable,
                            Name,
                            observedAt,
                            errorCode,
                            $"Gemini returned HTTP {(int)response.StatusCode} ({response.StatusCode}).{detail}");
                    }

                    using var document = await JsonDocument.ParseAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                    var text = GetResponseText(document.RootElement);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return InvalidResponse(observedAt, "Gemini returned no structured text.");
                    }

                    LanguageModelResponseDto? parsed;
                    try
                    {
                        parsed = JsonSerializer.Deserialize<LanguageModelResponseDto>(
                            text, JsonOptions);
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
                    var findings = ParseFindings(parsed.Findings, knownEvidence);

                    return ProviderResult<LanguageModelResponse>.AvailableResult(
                        new LanguageModelResponse(settings.GeminiModel, parsed.Summary, findings,
                            parsed.Conflicts!, DateTimeOffset.UtcNow, parsed.SelectedPlayerIds ?? []),
                        Name, observedAt, observedAt.AddMinutes(settings.GeminiResultTtlMinutes));
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

    private LanguageModelFinding[] ParseFindings(
        IEnumerable<JsonElement> rawFindings,
        IReadOnlySet<string> knownEvidence)
    {
        var findings = new List<LanguageModelFinding>();
        foreach (var rawFinding in rawFindings)
        {
            if (rawFinding.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning(
                    "Gemini returned a finding with JSON kind {JsonKind}; skipping it.",
                    rawFinding.ValueKind);
                continue;
            }

            FindingDto? finding;
            try
            {
                finding = rawFinding.Deserialize<FindingDto>(JsonOptions);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Gemini returned a finding with an invalid shape; skipping it.");
                continue;
            }

            if (finding is null
                || string.IsNullOrWhiteSpace(finding.SubjectKey)
                || string.IsNullOrWhiteSpace(finding.Explanation))
            {
                continue;
            }

            var evidenceIds = (finding.EvidenceIds ?? [])
                .Where(knownEvidence.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (evidenceIds.Length == 0)
            {
                continue;
            }

            findings.Add(new LanguageModelFinding(
                finding.SubjectKey,
                finding.Explanation,
                Math.Clamp(finding.Confidence ?? 0m, 0m, 1m),
                evidenceIds,
                finding.Caveats ?? []));
        }

        return findings.ToArray();
    }

    private static string BuildPrompt(LanguageModelRequest request, int maxCharacters)
    {
        var limit = Math.Max(1, maxCharacters);
        var evidence = request.Evidence.Take(100).Select(x => new PromptEvidence(
            x.EvidenceId, x.SubjectKey, x.Fact, x.Source, x.ObservedAt, x.FreshUntil, x.Confidence)).ToList();
        var deterministic = ParseStructuredSummary(request.DeterministicSummary);
        const string deterministicContract = "Deterministic summary (authoritative; do not override): Deterministic facts, scores, priorities, conflicts, and unavailable signals are authoritative. Do not change, override, or invent them.";
        const string responseContract = "Return only a JSON object with summary, selectedPlayerIds, findings, and conflicts. selectedPlayerIds must contain only supplied shortlist player IDs. Findings must cite supplied evidence IDs.";

        string Compose(bool includeUser) =>
            $"{request.SystemInstruction}\n\n{deterministicContract}\n{responseContract}\n\nDeterministic data:\n{deterministic}\n\nEvidence:\n{JsonSerializer.Serialize(evidence)}"
            + (includeUser ? $"\n\n{request.UserPrompt}" : string.Empty);

        while (evidence.Count > 0 && Compose(includeUser: true).Length > limit)
            evidence.RemoveAt(evidence.Count - 1);
        while (Compose(includeUser: true).Length > limit && RemoveStructuredData(ref deterministic))
        {
        }

        var prompt = Compose(includeUser: false);
        if (prompt.Length <= limit && Compose(includeUser: true).Length <= limit)
            prompt = Compose(includeUser: true);
        return prompt.Length <= limit ? prompt : request.SystemInstruction[..Math.Min(request.SystemInstruction.Length, limit)];
    }

    private static System.Text.Json.Nodes.JsonNode ParseStructuredSummary(string summary) =>
        string.IsNullOrWhiteSpace(summary)
            ? new System.Text.Json.Nodes.JsonObject()
            : System.Text.Json.Nodes.JsonNode.Parse(summary) ?? new System.Text.Json.Nodes.JsonObject();

    private static async Task<HttpResponseMessage> SendAndDisposeAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        finally
        {
            request.Dispose();
        }
    }

    private static bool RemoveStructuredData(ref System.Text.Json.Nodes.JsonNode summary)
    {
        if (summary is not System.Text.Json.Nodes.JsonObject obj)
            return false;
        var array = obj.FirstOrDefault(x => x.Value is System.Text.Json.Nodes.JsonArray).Value as System.Text.Json.Nodes.JsonArray;
        if (array is not null && array.Count > 0)
        {
            array.RemoveAt(array.Count - 1);
            return true;
        }
        var property = obj.LastOrDefault();
        if (property.Key is not null)
        {
            obj.Remove(property.Key);
            return true;
        }
        return false;
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
        public List<string>? SelectedPlayerIds { get; set; }
        public List<JsonElement> Findings { get; set; } = [];
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
