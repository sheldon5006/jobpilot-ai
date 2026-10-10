using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobPilot.Api.Models;

namespace JobPilot.Api.Services;

public sealed class GeminiAnalysisService(HttpClient httpClient, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JobAnalysisResult> AnalyzeAsync(
        JobAnalysisRequest request,
        CandidateProfile profile,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["Gemini:ApiKey"] ?? configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key is not configured. Add Gemini:ApiKey to appsettings.Local.json or set GEMINI_API_KEY.");
        }

        var model = configuration["Gemini:Model"] ?? configuration["GEMINI_MODEL"];
        if (string.IsNullOrWhiteSpace(model))
        {
            model = "gemini-3.1-flash-lite";
        }

        var endpoint =
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";

        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = AnalysisPrompt.SystemInstruction } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = AnalysisPrompt.BuildUserPrompt(request, profile) } }
                }
            },
            generationConfig = new
            {
                responseFormat = new
                {
                    text = new
                    {
                        // The current responseFormat REST field is an enum; use its enum token, not the MIME label.
                        mimeType = "APPLICATION_JSON",
                        schema = AnalysisPrompt.OutputSchema()
                    }
                },
                // Deterministic sampling so the same vacancy and profile give the same assessment.
                temperature = 0,
                seed = 7,
                maxOutputTokens = 4096,
                thinkingConfig = new
                {
                    // Job matching is a classification task; lower reasoning effort reduces latency.
                    thinkingLevel = "LOW"
                }
            }
        };

        using var response = await SendWithRetriesAsync(
            endpoint,
            apiKey,
            payload,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new GeminiApiException(
                "Gemini's free-tier request or token limit was reached. Wait for the quota to reset and try again; DBot will not switch to a paid service.",
                (int)response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            var providerMessage = await TryReadProviderErrorMessageAsync(response, cancellationToken);
            var detail = string.IsNullOrWhiteSpace(providerMessage)
                ? $"Gemini returned HTTP {(int)response.StatusCode} after limited retries. This may be a temporary provider issue; try again later."
                : $"Gemini returned HTTP {(int)response.StatusCode}: {providerMessage}";

            throw new GeminiApiException(detail, (int)response.StatusCode);
        }

        using var responseJson = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (!responseJson.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            throw new GeminiApiException(
                "Gemini returned no analysable candidate response. Try again; if this continues, check the provider response and configured model.",
                (int)HttpStatusCode.BadGateway);
        }

        var candidate = candidates[0];
        if (candidate.TryGetProperty("finishReason", out var finishReasonElement) &&
            finishReasonElement.ValueKind == JsonValueKind.String &&
            string.Equals(finishReasonElement.GetString(), "MAX_TOKENS", StringComparison.OrdinalIgnoreCase))
        {
            throw new GeminiApiException(
                "Gemini reached its output-token limit before completing the job assessment. Try again; the response budget has been increased, and the output is limited to the most relevant evidence.",
                (int)HttpStatusCode.BadGateway);
        }

        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.GetArrayLength() == 0 ||
            !parts[0].TryGetProperty("text", out var textElement))
        {
            throw new GeminiApiException(
                "Gemini returned no analysable text. Try again with a shorter job description.",
                (int)HttpStatusCode.BadGateway);
        }

        var jsonText = textElement.GetString();
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            throw new GeminiApiException("Gemini returned an empty analysis.", (int)HttpStatusCode.BadGateway);
        }

        JobAnalysisResult? result;
        try
        {
            result = JsonSerializer.Deserialize<JobAnalysisResult>(jsonText, JsonOptions);
        }
        catch (JsonException ex)
        {
            // Keep the upstream payload private, but expose the JSON path/type mismatch
            // so local development can identify which field needs a more flexible schema.
            var path = string.IsNullOrWhiteSpace(ex.Path) ? "the response root" : ex.Path;
            var incompleteJson = ex.Message.Contains("end of data", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("end of input", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("incomplete", StringComparison.OrdinalIgnoreCase);
            var detail = incompleteJson
                ? $"Gemini returned incomplete JSON at {path}; the response may have been truncated. Try again. If it repeats, use a shorter job description."
                : $"Gemini returned JSON that does not match the expected schema at {path}. {ex.Message}";
            throw new GeminiApiException(detail, (int)HttpStatusCode.BadGateway);
        }

        if (result is null)
        {
            throw new GeminiApiException("Gemini returned an empty analysis.", (int)HttpStatusCode.BadGateway);
        }

        // Treat model output as untrusted and normalise missing/null fields before using them.
        result.KeyRequirements ??= [];
        result.CandidateExpectations ??= [];
        result.MatchedRequirements ??= [];
        result.Gaps ??= [];
        result.QuestionsToVerify ??= [];
        result.DetectedLanguage ??= "Unknown";
        result.EnglishSummary ??= string.Empty;
        result.Summary ??= string.Empty;
        result.Rationale ??= string.Empty;
        result.Recommendation = (result.Recommendation ?? "Review").Trim().ToLowerInvariant() switch
        {
            "apply" => "Apply",
            "skip" => "Skip",
            _ => "Review"
        };

        ProfileEvidenceValidator.RequireConflictEvidence(profile, result);
        JobRequirementRuleEngine.Apply(request, profile, result);
        VacancyRequirementValidator.Apply(request, result);
        ProfileEvidenceValidator.Apply(profile, result);
        JobFitScoreCalibrator.Apply(result);
        result.RequiresHumanReview = true;
        result.Note = "AI-assisted recommendation only. Check the evidence before applying; no application has been submitted.";
        return result;
    }
    private async Task<HttpResponseMessage> SendWithRetriesAsync(
        string endpoint,
        string apiKey,
        object payload,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
            message.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
            message.Content = JsonContent.Create(payload, options: JsonOptions);

            var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var isTransient = response.StatusCode == HttpStatusCode.RequestTimeout ||
                              (int)response.StatusCode >= 500;

            if (!isTransient || attempt == maxAttempts)
            {
                return response;
            }

            response.Dispose();

            // Exponential backoff (1s, 2s) plus small jitter; do not spin indefinitely.
            var baseDelaySeconds = Math.Pow(2, attempt - 1);
            var jitterMilliseconds = Random.Shared.Next(0, 251);
            var delay = TimeSpan.FromSeconds(baseDelaySeconds) +
                        TimeSpan.FromMilliseconds(jitterMilliseconds);

            await Task.Delay(delay, cancellationToken);
        }

        throw new InvalidOperationException("Gemini request retry loop ended unexpectedly.");
    }

    private static async Task<string?> TryReadProviderErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // Do not return arbitrary proxy or HTML error bodies to the client.
        }
        catch (IOException)
        {
            // The status code remains useful even if the error body cannot be read.
        }

        return null;
    }


}

public sealed class GeminiApiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
