namespace JobPilot.Api.Models;

/// <summary>
/// A job-specific CV generated from the candidate profile. Stored as JSON so the user
/// can edit it in DBot and download it as DOCX from the dashboard.
/// </summary>
public sealed class GeneratedCv
{
    public Guid JobId { get; set; }
    public string CvJson { get; set; } = "{}";
    public string CustomInstructions { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public SavedJob Job { get; set; } = null!;
}

public sealed class CvDocument
{
    public string Headline { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<CvSkillGroup> SkillGroups { get; set; } = [];
    public List<CvExperience> Experience { get; set; } = [];
    public List<CvProject> Projects { get; set; } = [];
    public List<string> Education { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public List<string> Certifications { get; set; } = [];
}

public sealed class CvSkillGroup
{
    public string Category { get; set; } = string.Empty;
    public List<string> Skills { get; set; } = [];
}

public sealed class CvExperience
{
    // Index into CandidateProfile.Experience; role and period are always copied from the profile.
    public int SourceIndex { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public List<string> Bullets { get; set; } = [];
}

public sealed class CvProject
{
    // Index into CandidateProfile.Projects; name, context, URL and technologies are copied from the profile.
    public int SourceIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public List<string> Bullets { get; set; } = [];
    public List<string> Technologies { get; set; } = [];
}

public sealed class GenerateCvRequest
{
    public string? CustomInstructions { get; init; }
}

public sealed class UpdateGeneratedCvRequest
{
    public CvDocument? Cv { get; init; }
}

public sealed record GeneratedCvResponse(
    Guid JobId,
    string JobTitle,
    string Company,
    string ApplicationStatus,
    ContactDetails Contact,
    CvDocument Cv,
    string CustomInstructions,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed class AnswerQuestionRequest
{
    public string Question { get; init; } = string.Empty;
    public Guid? JobId { get; init; }
    public string? JobTitle { get; init; }
    public string? Company { get; init; }
    public string? JobDescription { get; init; }
    public string? CustomInstructions { get; init; }
    public string? Length { get; init; }
}

public sealed record AnswerQuestionResponse(string Question, string Answer);
