using System.Text.Json;
using JobPilot.Api.Data;
using JobPilot.Api.Models;
using JobPilot.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Local-only settings file. Keep appsettings.Local.json untracked; never commit real API keys.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
if (databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
    databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = GetPostgresConnectionString(builder.Configuration);
    builder.Services.AddDbContext<JobPilotDbContext>(options => options.UseNpgsql(connectionString));
}
else if (databaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) ||
         databaseProvider.Equals("SQLite", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("JobPilot")
        ?? $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "jobpilot.db")}";
    builder.Services.AddDbContext<JobPilotDbContext>(options => options.UseSqlite(connectionString));
}
else
{
    throw new InvalidOperationException("Database:Provider must be Sqlite or Postgres.");
}

var configuredDashboardOrigin =
    builder.Configuration["DASHBOARD_ORIGIN"] ??
    builder.Configuration["Dashboard:Origin"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalDbotExtension", policy =>
        policy.SetIsOriginAllowed(origin =>
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                return false;
            }

            // Chrome extension popup.
            if (uri.Scheme.Equals("chrome-extension", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Local development origins for the extension and dashboard.
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // In production, allow only the explicitly configured dashboard origin.
            return !string.IsNullOrWhiteSpace(configuredDashboardOrigin) &&
                   string.Equals(
                       origin.TrimEnd('/'),
                       configuredDashboardOrigin.Trim().TrimEnd('/'),
                       StringComparison.OrdinalIgnoreCase);
        })
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddHttpClient<GeminiAnalysisService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
});

builder.Services.AddHttpClient<OllamaAnalysisService>((services, client) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["Ollama:BaseUrl"] ?? "http://127.0.0.1:11434";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(120);
});

var app = builder.Build();

// This is sufficient for the initial personal dashboard prototype. Use EF migrations
// when evolving the schema beyond this first version.
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<JobPilotDbContext>();
    await database.Database.EnsureCreatedAsync();
}

app.UseCors("LocalDbotExtension");

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "JobPilot.Api"
}));

app.MapGet("/api/jobs", async (JobPilotDbContext database, CancellationToken cancellationToken) =>
{
    var jobs = await database.SavedJobs
        .AsNoTracking()
        .OrderByDescending(job => job.UpdatedAtUtc)
        .Select(job => new SavedJobListItem(
            job.Id,
            job.JobTitle,
            job.Company,
            job.MatchScore,
            job.Recommendation,
            job.DetectedLanguage,
            job.Summary,
            job.ApplicationStatus,
            job.CreatedAtUtc,
            job.UpdatedAtUtc))
        .ToListAsync(cancellationToken);

    return Results.Ok(jobs);
});

app.MapGet("/api/jobs/{id:guid}", async (
    Guid id,
    JobPilotDbContext database,
    CancellationToken cancellationToken) =>
{
    var job = await database.SavedJobs.AsNoTracking()
        .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    if (job is null)
    {
        return Results.NotFound(new { title = "Saved job not found." });
    }

    JobAnalysisResult? analysis;
    try
    {
        analysis = JsonSerializer.Deserialize<JobAnalysisResult>(
            job.AnalysisJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    catch (JsonException)
    {
        return Results.Problem(
            title: "Saved analysis could not be read",
            detail: "The stored analysis is invalid. Run the analysis again for this job.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    if (analysis is null)
    {
        return Results.Problem(
            title: "Saved analysis is empty",
            detail: "Run the analysis again for this job.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    return Results.Ok(new SavedJobDetails(
        job.Id,
        job.JobTitle,
        job.Company,
        job.JobDescription,
        job.ApplicationStatus,
        job.Notes,
        job.CreatedAtUtc,
        job.UpdatedAtUtc,
        analysis));
});

app.MapPut("/api/jobs/{id:guid}", async (
    Guid id,
    UpdateSavedJobRequest request,
    JobPilotDbContext database,
    CancellationToken cancellationToken) =>
{
    var allowedStatuses = new[] { "Saved", "Applied", "Interview", "Rejected", "Offer" };
    var requestedStatus = request.ApplicationStatus?.Trim();

    if (string.IsNullOrWhiteSpace(requestedStatus) ||
        !allowedStatuses.Contains(requestedStatus, StringComparer.OrdinalIgnoreCase))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["applicationStatus"] = ["Choose Saved, Applied, Interview, Rejected, or Offer."]
        });
    }

    if ((request.Notes?.Length ?? 0) > 4000)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["notes"] = ["Notes must be 4,000 characters or fewer."]
        });
    }

    var job = await database.SavedJobs.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (job is null)
    {
        return Results.NotFound(new { title = "Saved job not found." });
    }

    job.ApplicationStatus = allowedStatuses.First(status =>
        status.Equals(requestedStatus, StringComparison.OrdinalIgnoreCase));
    job.Notes = request.Notes?.Trim() ?? string.Empty;
    job.UpdatedAtUtc = DateTime.UtcNow;
    await database.SaveChangesAsync(cancellationToken);

    return Results.Ok(new
    {
        job.Id,
        job.ApplicationStatus,
        job.Notes,
        job.UpdatedAtUtc
    });
});

app.MapPost("/api/jobs/analyze", AnalyzeJobAsync);

app.Run();

static string GetPostgresConnectionString(IConfiguration configuration)
{
    var configuredConnection = configuration.GetConnectionString("JobPilot");
    if (!string.IsNullOrWhiteSpace(configuredConnection))
    {
        return configuredConnection;
    }

    var databaseUrl = configuration["DATABASE_URL"];
    if (string.IsNullOrWhiteSpace(databaseUrl))
    {
        throw new InvalidOperationException(
            "PostgreSQL was selected, but ConnectionStrings:JobPilot or DATABASE_URL is not configured.");
    }

    // Managed platforms such as Render and Neon commonly provide a PostgreSQL URI.
    if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri) ||
        !(uri.Scheme.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
          uri.Scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase)))
    {
        // Also accept the standard ADO.NET key/value connection-string format.
        return databaseUrl;
    }

    var credentials = uri.UserInfo.Split(':', 2);
    if (credentials.Length != 2)
    {
        throw new InvalidOperationException("DATABASE_URL must include a PostgreSQL username and password.");
    }

    return new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')),
        Username = Uri.UnescapeDataString(credentials[0]),
        Password = Uri.UnescapeDataString(credentials[1]),
        SslMode = SslMode.Require,
        Timeout = 15,
        CommandTimeout = 30,
        Pooling = true
    }.ConnectionString;
}

static async Task<IResult> AnalyzeJobAsync(
    JobAnalysisRequest request,
    GeminiAnalysisService geminiAnalyzer,
    OllamaAnalysisService ollamaAnalyzer,
    JobPilotDbContext database,
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

    var provider = configuration["AI:Provider"] ?? "Gemini";
    var useOllama = provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);
    var useGemini = provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase);

    if (!useOllama && !useGemini)
    {
        return Results.Problem(
            title: "AI provider is not supported",
            detail: "Set AI:Provider to either Gemini or Ollama in services/api/appsettings.Local.json.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (useGemini &&
        string.IsNullOrWhiteSpace(configuration["Gemini:ApiKey"]) &&
        string.IsNullOrWhiteSpace(configuration["GEMINI_API_KEY"]))
    {
        return Results.Problem(
            title: "Gemini API key is not configured",
            detail: "Gemini is selected. Add your key under Gemini:ApiKey in services/api/appsettings.Local.json, or set GEMINI_API_KEY. For fully local analysis, set AI:Provider to Ollama instead.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        var normalizedRequest = new JobAnalysisRequest
        {
            JobTitle = request.JobTitle?.Trim(),
            Company = request.Company?.Trim(),
            JobDescription = description
        };

        var result = useOllama
            ? await ollamaAnalyzer.AnalyzeAsync(normalizedRequest, profile, cancellationToken)
            : await geminiAnalyzer.AnalyzeAsync(normalizedRequest, profile, cancellationToken);

        if (!request.SaveToHistory)
        {
            return Results.Ok(result);
        }

        var savedJob = new SavedJob
        {
            JobTitle = string.IsNullOrWhiteSpace(request.JobTitle) ? "Untitled role" : request.JobTitle.Trim(),
            Company = request.Company?.Trim() ?? string.Empty,
            JobDescription = description,
            MatchScore = Math.Clamp(result.MatchScore, 0, 100),
            Recommendation = result.Recommendation,
            DetectedLanguage = result.DetectedLanguage,
            Summary = result.Summary,
            ApplicationStatus = "Saved",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        result.JobId = savedJob.Id;
        result.AnalyzedAtUtc = savedJob.CreatedAtUtc;
        savedJob.AnalysisJson = JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        database.SavedJobs.Add(savedJob);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Ok(result);
    }
    catch (OllamaApiException exception)
    {
        return Results.Problem(
            title: "Local AI analysis failed",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
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
        var detail = useOllama
            ? "Ollama could not be reached. Confirm the Ollama app is running and that its local API is available at the configured Ollama:BaseUrl."
            : "Gemini could not be reached. Check your internet connection and try again. No paid-provider fallback is configured.";

        return Results.Problem(
            title: useOllama ? "Local Ollama service could not be reached" : "Gemini could not be reached",
            detail: detail,
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        var detail = useOllama
            ? "The local model did not respond in time. Try a shorter job description or check Ollama resource usage."
            : "Gemini did not respond in time. Try again with a shorter description.";

        return Results.Problem(
            title: "AI analysis timed out",
            detail: detail,
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
}
