using System.Text;
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

// Ensure new tables also appear in databases created by an earlier prototype build.
await EnsureDatabaseReadyAsync(app.Services, builder.Environment, builder.Configuration);

app.UseCors("LocalDbotExtension");

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "JobPilot.Api"
}));

app.MapGet("/api/profile", async (JobPilotDbContext database, CancellationToken cancellationToken) =>
{
    var document = await database.CandidateProfiles.AsNoTracking()
        .FirstOrDefaultAsync(item => item.Id == "primary", cancellationToken);

    if (document is null)
    {
        return Results.Problem(
            title: "Candidate profile is not available",
            detail: "Save your career story from My Profile before analysing a job.",
            statusCode: StatusCodes.Status409Conflict);
    }

    try
    {
        var profile = JsonSerializer.Deserialize<CandidateProfile>(
            document.ProfileJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return Results.Ok(new CandidateProfileResponse(profile ?? new CandidateProfile(), document.UpdatedAtUtc));
    }
    catch (JsonException)
    {
        return Results.Problem(
            title: "Saved candidate profile could not be read",
            detail: "Open My Profile and save the profile again.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPut("/api/profile", async (
    CandidateProfile submittedProfile,
    JobPilotDbContext database,
    CancellationToken cancellationToken) =>
{
    var errors = ValidateCandidateProfile(submittedProfile);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var profile = NormalizeCandidateProfile(submittedProfile);
    var profileJson = JsonSerializer.Serialize(profile, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    if (profileJson.Length > 250_000)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["profile"] = ["The complete profile must be smaller than 250,000 characters."]
        });
    }

    var document = await database.CandidateProfiles
        .FirstOrDefaultAsync(item => item.Id == "primary", cancellationToken);

    if (document is null)
    {
        document = new CandidateProfileDocument { Id = "primary" };
        database.CandidateProfiles.Add(document);
    }

    document.ProfileJson = profileJson;
    document.UpdatedAtUtc = DateTime.UtcNow;
    await database.SaveChangesAsync(cancellationToken);

    return Results.Ok(new CandidateProfileResponse(profile, document.UpdatedAtUtc));
});

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
            job.CvAttachment != null,
            job.CvAttachment == null ? null : job.CvAttachment.FileName,
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

    var cvMetadata = await database.JobCvAttachments.AsNoTracking()
        .Where(attachment => attachment.JobId == id)
        .Select(attachment => new { attachment.FileName, attachment.UploadedAtUtc, attachment.SizeBytes })
        .FirstOrDefaultAsync(cancellationToken);

    return Results.Ok(new SavedJobDetails(
        job.Id,
        job.JobTitle,
        job.Company,
        job.JobDescription,
        job.ApplicationStatus,
        job.Notes,
        cvMetadata?.FileName,
        cvMetadata?.UploadedAtUtc,
        cvMetadata?.SizeBytes,
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


app.MapPost("/api/jobs/{id:guid}/cv", async (
    Guid id,
    HttpRequest request,
    JobPilotDbContext database,
    CancellationToken cancellationToken) =>
{
    const long maxCvBytes = 10L * 1024 * 1024;

    if (!request.HasFormContentType)
    {
        return Results.Problem(
            title: "CV upload must use multipart form data",
            detail: "Choose a PDF or DOCX file and try again.",
            statusCode: StatusCodes.Status415UnsupportedMediaType);
    }

    if (request.ContentLength > maxCvBytes + 128_000)
    {
        return Results.Problem(
            title: "CV file is too large",
            detail: "The maximum CV file size is 10 MB.",
            statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["file"] = ["Choose a PDF or DOCX CV to attach."]
        });
    }

    if (file.Length > maxCvBytes)
    {
        return Results.Problem(
            title: "CV file is too large",
            detail: "The maximum CV file size is 10 MB.",
            statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (extension is not (".pdf" or ".docx"))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["file"] = ["Only PDF and DOCX CV files are supported."]
        });
    }

    await using var fileStream = file.OpenReadStream();
    using var memoryStream = new MemoryStream();
    await fileStream.CopyToAsync(memoryStream, cancellationToken);
    var bytes = memoryStream.ToArray();

    if (extension == ".pdf" &&
        (bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-"))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["file"] = ["The selected file does not appear to be a valid PDF."]
        });
    }

    if (extension == ".docx" &&
        (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["file"] = ["The selected file does not appear to be a valid DOCX document."]
        });
    }

    var jobExists = await database.SavedJobs.AnyAsync(job => job.Id == id, cancellationToken);
    if (!jobExists)
    {
        return Results.NotFound(new { title = "Saved job not found." });
    }

    var attachment = await database.JobCvAttachments
        .FirstOrDefaultAsync(item => item.JobId == id, cancellationToken);
    if (attachment is null)
    {
        attachment = new JobCvAttachment { JobId = id };
        database.JobCvAttachments.Add(attachment);
    }

    var safeFileName = Path.GetFileName(file.FileName);
    if (safeFileName.Length > 255)
    {
        safeFileName = safeFileName[^255..];
    }

    attachment.FileName = safeFileName;
    attachment.ContentType = extension == ".pdf"
        ? "application/pdf"
        : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    attachment.Bytes = bytes;
    attachment.SizeBytes = bytes.LongLength;
    attachment.UploadedAtUtc = DateTime.UtcNow;
    await database.SaveChangesAsync(cancellationToken);

    return Results.Ok(new
    {
        attachment.FileName,
        attachment.SizeBytes,
        attachment.UploadedAtUtc
    });
});

app.MapGet("/api/jobs/{id:guid}/cv", async (
    Guid id,
    JobPilotDbContext database,
    CancellationToken cancellationToken) =>
{
    var attachment = await database.JobCvAttachments.AsNoTracking()
        .FirstOrDefaultAsync(item => item.JobId == id, cancellationToken);

    if (attachment is null)
    {
        return Results.NotFound(new { title = "No CV is attached to this job." });
    }

    return Results.File(attachment.Bytes, attachment.ContentType, attachment.FileName);
});

app.MapDelete("/api/jobs/{id:guid}/cv", async (
    Guid id,
    JobPilotDbContext database,
    CancellationToken cancellationToken) =>
{
    var attachment = await database.JobCvAttachments
        .FirstOrDefaultAsync(item => item.JobId == id, cancellationToken);

    if (attachment is null)
    {
        return Results.NotFound(new { title = "No CV is attached to this job." });
    }

    database.JobCvAttachments.Remove(attachment);
    await database.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
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

    var profileDocument = await database.CandidateProfiles.AsNoTracking()
        .FirstOrDefaultAsync(item => item.Id == "primary", cancellationToken);

    CandidateProfile? profile;
    try
    {
        profile = profileDocument is null
            ? null
            : JsonSerializer.Deserialize<CandidateProfile>(
                profileDocument.ProfileJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    catch (JsonException)
    {
        return Results.Problem(
            title: "Saved candidate profile could not be read",
            detail: "Open My Profile and save the profile again.",
            statusCode: StatusCodes.Status409Conflict);
    }

    if (profile is null ||
        string.IsNullOrWhiteSpace(profile.ProfessionalSummary) ||
        profile.ProfessionalSkills is null ||
        profile.ProfessionalSkills.Count == 0)
    {
        return Results.Problem(
            title: "Candidate profile needs to be completed",
            detail: "Open My Profile and add a professional summary and at least one professional skill before analysing jobs.",
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


static async Task EnsureDatabaseReadyAsync(
    IServiceProvider services,
    IWebHostEnvironment environment,
    IConfiguration configuration)
{
    await using var scope = services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<JobPilotDbContext>();
    await database.Database.EnsureCreatedAsync();

    var providerName = database.Database.ProviderName ?? string.Empty;
    if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CandidateProfiles" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_CandidateProfiles" PRIMARY KEY,
                "ProfileJson" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            """);

        await database.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "JobCvAttachments" (
                "JobId" TEXT NOT NULL CONSTRAINT "PK_JobCvAttachments" PRIMARY KEY,
                "FileName" TEXT NOT NULL,
                "ContentType" TEXT NOT NULL,
                "Bytes" BLOB NOT NULL,
                "SizeBytes" INTEGER NOT NULL,
                "UploadedAtUtc" TEXT NOT NULL,
                CONSTRAINT "FK_JobCvAttachments_SavedJobs_JobId"
                    FOREIGN KEY ("JobId") REFERENCES "SavedJobs" ("Id") ON DELETE CASCADE
            );
            """);
    }
    else if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
    {
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CandidateProfiles" (
                "Id" character varying(32) NOT NULL CONSTRAINT "PK_CandidateProfiles" PRIMARY KEY,
                "ProfileJson" text NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL
            );
            """);

        await database.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "JobCvAttachments" (
                "JobId" uuid NOT NULL CONSTRAINT "PK_JobCvAttachments" PRIMARY KEY,
                "FileName" character varying(255) NOT NULL,
                "ContentType" character varying(160) NOT NULL,
                "Bytes" bytea NOT NULL,
                "SizeBytes" bigint NOT NULL,
                "UploadedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_JobCvAttachments_SavedJobs_JobId"
                    FOREIGN KEY ("JobId") REFERENCES "SavedJobs" ("Id") ON DELETE CASCADE
            );
            """);
    }
    else
    {
        throw new InvalidOperationException($"Database provider '{providerName}' is not supported.");
    }

    var existingProfile = await database.CandidateProfiles
        .AnyAsync(item => item.Id == "primary");
    if (existingProfile)
    {
        return;
    }

    var configuredPath = configuration["DBOT_PROFILE_PATH"];
    var profilePath = string.IsNullOrWhiteSpace(configuredPath)
        ? Path.Combine(environment.ContentRootPath, "candidate-profile.json")
        : Path.GetFullPath(configuredPath, environment.ContentRootPath);

    CandidateProfile profile = new();
    if (File.Exists(profilePath))
    {
        try
        {
            await using var profileStream = File.OpenRead(profilePath);
            profile = await JsonSerializer.DeserializeAsync<CandidateProfile>(
                profileStream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new CandidateProfile();
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Console.Error.WriteLine($"Could not import the local candidate profile: {exception.Message}");
        }
    }

    database.CandidateProfiles.Add(new CandidateProfileDocument
    {
        Id = "primary",
        ProfileJson = JsonSerializer.Serialize(profile, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        UpdatedAtUtc = DateTime.UtcNow
    });
    await database.SaveChangesAsync();
}

static Dictionary<string, string[]> ValidateCandidateProfile(CandidateProfile profile)
{
    var errors = new Dictionary<string, string[]>();
    void AddError(string field, string message) => errors[field] = [message];

    if ((profile.ProfessionalSummary?.Length ?? 0) > 6000)
        AddError("professionalSummary", "The professional summary must be 6,000 characters or fewer.");
    if ((profile.WorkAuthorization?.Length ?? 0) > 500)
        AddError("workAuthorization", "Work authorization must be 500 characters or fewer.");
    if ((profile.TargetRoles?.Count ?? 0) > 100 ||
        (profile.ProfessionalSkills?.Count ?? 0) > 100 ||
        (profile.ProjectAndAcademicSkills?.Count ?? 0) > 100 ||
        (profile.Education?.Count ?? 0) > 100 ||
        (profile.Certifications?.Count ?? 0) > 100 ||
        (profile.Constraints?.Count ?? 0) > 100)
        AddError("lists", "Each list can contain at most 100 entries.");
    if ((profile.Experience?.Count ?? 0) > 50)
        AddError("experience", "The career timeline can contain at most 50 roles.");
    if ((profile.Languages?.Count ?? 0) > 50)
        AddError("languages", "The profile can contain at most 50 languages.");

    var ordinaryLists = new[]
    {
        profile.TargetRoles, profile.ProfessionalSkills, profile.ProjectAndAcademicSkills,
        profile.Education, profile.Certifications, profile.Constraints
    };
    if (ordinaryLists.SelectMany(list => list ?? []).Any(value => (value?.Length ?? 0) > 2000))
        AddError("listEntry", "Individual list entries must be 2,000 characters or fewer.");

    if ((profile.Experience ?? []).Any(item =>
        (item.Role?.Length ?? 0) > 160 ||
        (item.Period?.Length ?? 0) > 120 ||
        (item.Evidence?.Count ?? 0) > 50 ||
        (item.Evidence ?? []).Any(evidence => (evidence?.Length ?? 0) > 3000)))
        AddError("experienceEntry", "Each role needs a title of 160 characters or fewer, a period of 120 characters or fewer, and up to 50 evidence lines of 3,000 characters each.");

    if ((profile.Languages ?? []).Any(item =>
        (item.Language?.Length ?? 0) > 100 || (item.Proficiency?.Length ?? 0) > 160))
        AddError("languageEntry", "Language names must be 100 characters or fewer and proficiency descriptions 160 characters or fewer.");

    return errors;
}

static CandidateProfile NormalizeCandidateProfile(CandidateProfile profile)
{
    static string Clean(string? value) => value?.Trim() ?? string.Empty;
    static List<string> CleanList(IEnumerable<string>? values) =>
        (values ?? []).Select(Clean).Where(value => value.Length > 0).ToList();

    return new CandidateProfile
    {
        ProfessionalSummary = Clean(profile.ProfessionalSummary),
        TargetRoles = CleanList(profile.TargetRoles),
        ProfessionalSkills = CleanList(profile.ProfessionalSkills),
        ProjectAndAcademicSkills = CleanList(profile.ProjectAndAcademicSkills),
        Experience = (profile.Experience ?? [])
            .Where(item => item is not null)
            .Select(item => new ExperienceEntry
            {
                Role = Clean(item.Role),
                Period = Clean(item.Period),
                Evidence = CleanList(item.Evidence)
            })
            .Where(item => item.Role.Length > 0 || item.Period.Length > 0 || item.Evidence.Count > 0)
            .ToList(),
        Education = CleanList(profile.Education),
        Languages = (profile.Languages ?? [])
            .Where(item => item is not null)
            .Select(item => new LanguageEntry
            {
                Language = Clean(item.Language),
                Proficiency = Clean(item.Proficiency)
            })
            .Where(item => item.Language.Length > 0 || item.Proficiency.Length > 0)
            .ToList(),
        WorkAuthorization = Clean(profile.WorkAuthorization),
        Certifications = CleanList(profile.Certifications),
        Constraints = CleanList(profile.Constraints)
    };
}
