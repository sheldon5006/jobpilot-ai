using System.Text.Json;
using JobPilot.Api.Models;
using JobPilot.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalDbotExtension", policy =>
        policy.SetIsOriginAllowed(origin =>
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                return false;
            }

            // Local-development policy only. The API is intended to bind to loopback.
            if (uri.Scheme.Equals("chrome-extension", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                   (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase));
        })
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddHttpClient<GeminiAnalysisService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

var app = builder.Build();

app.UseCors("LocalDbotExtension");

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "JobPilot.Api"
}));

app.MapPost("/api/jobs/analyze", AnalyzeJobAsync);

app.Run();

static async Task<IResult> AnalyzeJobAsync(
    JobAnalysisRequest request,
    GeminiAnalysisService analyzer,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    CancellationToken cancellationToken)
{
    var description = request.JobDescription?.Trim() ?? string.Empty;

    if (description.Length < 40)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["jobDescription"] = ["Paste at least 40 characters from the job description."]
        });
    }

    if (description.Length > 20_000)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["jobDescription"] = ["The job description must be 20,000 characters or fewer."]
        });
    }

    if ((request.JobTitle?.Length ?? 0) > 160 || (request.Company?.Length ?? 0) > 160)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["jobTitle/company"] = ["Job title and company must each be 160 characters or fewer."]
        });
    }

    var configuredPath = configuration["DBOT_PROFILE_PATH"];
    var profilePath = string.IsNullOrWhiteSpace(configuredPath)
        ? Path.Combine(environment.ContentRootPath, "candidate-profile.json")
        : Path.GetFullPath(configuredPath, environment.ContentRootPath);

    if (!File.Exists(profilePath))
    {
        return Results.Problem(
            title: "Candidate profile is not configured",
            detail: "Copy candidate-profile.example.json to candidate-profile.json in services/api, then fill it with your accurate information. The local candidate profile is ignored by Git.",
            statusCode: StatusCodes.Status409Conflict);
    }

    CandidateProfile? profile;
    try
    {
        await using var profileStream = File.OpenRead(profilePath);
        profile = await JsonSerializer.DeserializeAsync<CandidateProfile>(
            profileStream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken);
    }
    catch (JsonException)
    {
        return Results.Problem(
            title: "Candidate profile JSON is invalid",
            detail: "Check the syntax of services/api/candidate-profile.json.",
            statusCode: StatusCodes.Status409Conflict);
    }
    catch (IOException)
    {
        return Results.Problem(
            title: "Candidate profile could not be read",
            detail: "Check the file path and permissions for the local candidate profile.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    if (profile is null ||
        string.IsNullOrWhiteSpace(profile.ProfessionalSummary) ||
        profile.ProfessionalSummary.Contains("Replace this text", StringComparison.OrdinalIgnoreCase) ||
        profile.ProfessionalSkills.Count == 0)
    {
        return Results.Problem(
            title: "Candidate profile needs to be completed",
            detail: "Add a factual professional summary and at least one professional skill to candidate-profile.json before analysing jobs.",
            statusCode: StatusCodes.Status409Conflict);
    }

    if (string.IsNullOrWhiteSpace(configuration["GEMINI_API_KEY"]))
    {
        return Results.Problem(
            title: "Gemini API key is not configured",
            detail: "Set GEMINI_API_KEY in the current PowerShell session before starting the API. The key belongs on the backend and must not be added to the extension or committed to Git.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        var result = await analyzer.AnalyzeAsync(
            new JobAnalysisRequest
            {
                JobTitle = request.JobTitle?.Trim(),
                Company = request.Company?.Trim(),
                JobDescription = description
            },
            profile,
            cancellationToken);

        return Results.Ok(result);
    }
    catch (GeminiApiException exception) when (exception.StatusCode == StatusCodes.Status429TooManyRequests)
    {
        return Results.Problem(
            title: "Gemini free-tier limit reached",
            detail: exception.Message,
            statusCode: StatusCodes.Status429TooManyRequests);
    }
    catch (GeminiApiException exception)
    {
        return Results.Problem(
            title: "AI analysis failed",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (HttpRequestException)
    {
        return Results.Problem(
            title: "Gemini could not be reached",
            detail: "Check your internet connection and try again. No paid-provider fallback is configured.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(
            title: "AI analysis timed out",
            detail: "Gemini did not respond in time. Try again with a shorter description.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
}
