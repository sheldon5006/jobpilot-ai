namespace JobPilot.Api.Models;

public sealed class CandidateProfile
{
    public string ProfessionalSummary { get; init; } = string.Empty;
    public List<string> TargetRoles { get; init; } = [];
    public List<string> ProfessionalSkills { get; init; } = [];
    public List<string> ProjectAndAcademicSkills { get; init; } = [];
    public List<ExperienceEntry> Experience { get; init; } = [];
    public List<string> Education { get; init; } = [];
    public List<LanguageEntry> Languages { get; init; } = [];
    public List<string> Constraints { get; init; } = [];
}

public sealed class ExperienceEntry
{
    public string Role { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public List<string> Evidence { get; init; } = [];
}

public sealed class LanguageEntry
{
    public string Language { get; init; } = string.Empty;
    public string Proficiency { get; init; } = string.Empty;
}
