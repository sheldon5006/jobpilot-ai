namespace JobPilot.Api.Models;

public sealed class JobAnalysisRequest
{
    public string? JobTitle { get; init; }
    public string? Company { get; init; }
    public string JobDescription { get; init; } = string.Empty;
}

public sealed class JobAnalysisResult
{
    public string Recommendation { get; set; } = "Review";
    public int MatchScore { get; set; }
    public string DetectedLanguage { get; set; } = "Unknown";
    public string EnglishSummary { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<MatchedRequirement> MatchedRequirements { get; set; } = [];
    public List<RequirementGap> Gaps { get; set; } = [];
    public List<string> QuestionsToVerify { get; set; } = [];
    public string Rationale { get; set; } = string.Empty;
    public string MandatoryRequirementsStatus { get; set; } = "Unknown";
    public List<string> EvidenceValidationWarnings { get; set; } = [];
    public List<string> RequirementValidationWarnings { get; set; } = [];

    // DBot provides decision support only; it never submits an application.
    public bool RequiresHumanReview { get; set; } = true;
    public string Note { get; set; } = "Recommendation only. No application has been submitted.";
}

public sealed class MatchedRequirement
{
    public string Requirement { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public List<string> EvidenceIds { get; set; } = [];
}

public sealed class RequirementGap
{
    public string Requirement { get; set; } = string.Empty;
    public string Severity { get; set; } = "Unknown";

    // Unverified means the profile does not establish the requirement.
    // Unmet means the profile explicitly conflicts with the requirement.
    public string Status { get; set; } = "Unverified";

    public string Explanation { get; set; } = string.Empty;
}
