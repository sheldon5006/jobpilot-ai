namespace JobPilot.Api.Models;

public sealed class JobAnalysisRequest
{
    public string? JobTitle { get; init; }
    public string? Company { get; init; }
    public string JobDescription { get; init; } = string.Empty;
    public bool SaveToHistory { get; init; } = true;
}

public sealed class JobAnalysisResult
{
    // Filled by the API when this analysis is saved to history.
    public Guid? JobId { get; set; }
    public DateTime? AnalyzedAtUtc { get; set; }
    public string Recommendation { get; set; } = "Review";
    public int MatchScore { get; set; }
    public string DetectedLanguage { get; set; } = "Unknown";
    public string EnglishSummary { get; set; } = string.Empty;
    public List<string> KeyRequirements { get; set; } = [];
    public List<string> CandidateExpectations { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
    public List<MatchedRequirement> MatchedRequirements { get; set; } = [];
    public List<RequirementGap> Gaps { get; set; } = [];
    public List<string> QuestionsToVerify { get; set; } = [];
    public string Rationale { get; set; } = string.Empty;
    public string MandatoryRequirementsStatus { get; set; } = "Unknown";
    public List<string> EvidenceValidationWarnings { get; set; } = [];
    public List<string> RequirementValidationWarnings { get; set; } = [];

    // How the evidence-based fit score was computed. Filled by the API, never by the model.
    public FitScoreBreakdown? ScoreBreakdown { get; set; }

    // DBot provides decision support only; it never submits an application.
    public bool RequiresHumanReview { get; set; } = true;
    public string Note { get; set; } = "Recommendation only. No application has been submitted.";
}

public sealed class MatchedRequirement
{
    public string Requirement { get; set; } = string.Empty;

    // Must-have, Preferred or Unknown, as stated in the vacancy.
    public string Importance { get; set; } = "Unknown";

    // The exact vacancy wording this requirement comes from (original language); used to verify it.
    public string VacancyQuote { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public List<string> EvidenceIds { get; set; } = [];

    // Professional, Internship, Project or None; derived by the API from the cited facts.
    public string EvidenceStrength { get; set; } = string.Empty;
}

public sealed class FitScoreBreakdown
{
    public int MustHaveMet { get; set; }
    public int MustHaveTotal { get; set; }
    public int PreferredMet { get; set; }
    public int PreferredTotal { get; set; }
    public int ProfessionalEvidence { get; set; }
    public int InternshipEvidence { get; set; }
    public int ProjectEvidence { get; set; }

    // Score before any mandatory-gap cap was applied.
    public int CoverageScore { get; set; }
    public string? CapReason { get; set; }
    public string Confidence { get; set; } = "Normal";
}

public sealed class RequirementGap
{
    public string Requirement { get; set; } = string.Empty;
    public string Severity { get; set; } = "Unknown";

    // Unverified means the profile does not establish the requirement.
    // Unmet means the profile explicitly conflicts with the requirement.
    public string Status { get; set; } = "Unverified";

    // The exact vacancy wording this requirement comes from (original language); used to verify it.
    public string VacancyQuote { get; set; } = string.Empty;

    // For Unmet: the profile facts that contradict the requirement. Unmet without them becomes Unverified.
    public List<string> ConflictEvidenceIds { get; set; } = [];

    public string Explanation { get; set; } = string.Empty;
}
