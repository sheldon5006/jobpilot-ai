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
    public string? CvFileName { get; set; }
    public string? CvContentType { get; set; }
    public byte[]? CvBytes { get; set; }
    public DateTime? CvUploadedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
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
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    JobAnalysisResult Analysis);

public sealed class UpdateSavedJobRequest
{
    public string? ApplicationStatus { get; init; }
    public string? Notes { get; init; }
}
