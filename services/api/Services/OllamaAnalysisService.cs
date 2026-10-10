using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobPilot.Api.Models;

namespace JobPilot.Api.Services;

public sealed class OllamaAnalysisService(HttpClient httpClient, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JobAnalysisResult> AnalyzeAsync(
        JobAnalysisRequest request,
        CandidateProfile profile,
        CancellationToken cancellationToken)
    {
        var model = configuration["Ollama:Model"] ?? "qwen3:4b";

        var payload = new
        {
            model,
            system = AnalysisPrompt.SystemInstruction,
            prompt = AnalysisPrompt.BuildUserPrompt(request, profile),
            stream = false,
            think = false,
            // Ollama accepts a JSON Schema as the format, constraining field names and enums.
            format = AnalysisPrompt.OutputSchema(),
            options = new
            {
                // Deterministic sampling so the same vacancy and profile give the same assessment.
                temperature = 0,
                seed = 7,
                num_predict = 1600,
                num_ctx = 8192
            },
            keep_alive = "10m"
        };

        using var response = await httpClient.PostAsJsonAsync(
            "api/generate",
            payload,
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var providerDetail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (providerDetail.Length > 700)
            {
                providerDetail = providerDetail[..700];
            }

            throw new OllamaApiException(
                $"Ollama returned HTTP {(int)response.StatusCode}: {providerDetail}",
                (int)response.StatusCode);
        }

        using var responseJson = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (!responseJson.RootElement.TryGetProperty("response", out var responseElement) ||
            responseElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(responseElement.GetString()))
        {
            throw new OllamaApiException(
                "Ollama returned no analysable text. Try again with a shorter job description.",
                (int)HttpStatusCode.BadGateway);
        }

        JobAnalysisResult? result;
        try
        {
            result = JsonSerializer.Deserialize<JobAnalysisResult>(responseElement.GetString()!, JsonOptions);
        }
        catch (JsonException ex)
        {
            var path = string.IsNullOrWhiteSpace(ex.Path) ? "the response root" : ex.Path;
            throw new OllamaApiException(
                $"Ollama returned JSON that does not match the expected schema at {path}. {ex.Message}",
                (int)HttpStatusCode.BadGateway);
        }

        if (result is null)
        {
            throw new OllamaApiException("Ollama returned an empty analysis.", (int)HttpStatusCode.BadGateway);
        }

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

        // Reconcile model output with requirements explicitly stated in the vacancy first,
        // so deterministic eligibility rules can replace irrelevant model-generated entries.
        ProfileEvidenceValidator.RequireConflictEvidence(profile, result);
        JobRequirementRuleEngine.Apply(request, profile, result);
        VacancyRequirementValidator.Apply(request, result);

        // Validate the remaining matches, including deterministic matches, against profile facts.
        ProfileEvidenceValidator.Apply(profile, result);

        JobFitScoreCalibrator.Apply(result);
        result.RequiresHumanReview = true;
        result.Note = "AI-assisted recommendation only. Check the evidence before applying; no application has been submitted.";
        return result;
    }


}

public sealed class OllamaApiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
