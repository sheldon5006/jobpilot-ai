namespace JobPilot.Api.Models;

public sealed class SavedJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string JobTitle { get; set; } = "Untitled role";
    public string Company { get; set; } = string.Empty;
    public string JobDescription { get; set; } = string.Empty;
    public int MatchScore { get; set; }
    public string Recommendation { get; set; } = "Review";
    public string DetectedLanguage { get; set; } = "Unknown";
    public string Summary { get; set; } = string.Empty;
    public string ApplicationStatus { get; set; } = "Saved";
    public string Notes { get; set; } = string.Empty;
    public string AnalysisJson { get; set; } = "{}";
    public JobCvAttachment? CvAttachment { get; set; }
    public GeneratedCv? GeneratedCv { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class ApplicationStatuses
{
    public const string Saved = "Saved";

    // A tailored CV was generated, so the user plans to apply.
    public const string Attempt = "Attempt";

    public static readonly string[] All = [Saved, Attempt, "Applied", "Interview", "Rejected", "Offer"];
}

public sealed class JobCvAttachment
{
    public Guid JobId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public byte[] Bytes { get; set; } = [];
    public long SizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public SavedJob Job { get; set; } = null!;
}

public sealed record SavedJobListItem(
    Guid Id,
    string JobTitle,
    string Company,
    int MatchScore,
    string Recommendation,
    string DetectedLanguage,
    string Summary,
    string ApplicationStatus,
    bool HasCv,
    string? CvFileName,
    bool HasGeneratedCv,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record SavedJobDetails(
    Guid Id,
    string JobTitle,
    string Company,
    string JobDescription,
    string ApplicationStatus,
    string Notes,
    string? CvFileName,
    DateTime? CvUploadedAtUtc,
    long? CvSizeBytes,
    DateTime? GeneratedCvUpdatedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    JobAnalysisResult Analysis);

public sealed class UpdateSavedJobRequest
{
    public string? ApplicationStatus { get; init; }
    public string? Notes { get; init; }
}
