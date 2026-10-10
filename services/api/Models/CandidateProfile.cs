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
    public string WorkAuthorization { get; init; } = string.Empty;
    public List<string> Certifications { get; init; } = [];
    public List<string> Constraints { get; init; } = [];

    // Shown on generated CVs and in DBot's profile tab. Never sent to the AI provider.
    public ContactDetails Contact { get; init; } = new();

    /// <summary>Returns a copy without personal contact details, for prompts sent to an AI provider.</summary>
    public CandidateProfile WithoutContact() => new()
    {
        ProfessionalSummary = ProfessionalSummary,
        TargetRoles = TargetRoles,
        ProfessionalSkills = ProfessionalSkills,
        ProjectAndAcademicSkills = ProjectAndAcademicSkills,
        Experience = Experience,
        Education = Education,
        Languages = Languages,
        WorkAuthorization = WorkAuthorization,
        Certifications = Certifications,
        Constraints = Constraints,
        Contact = new ContactDetails()
    };
}

public sealed class ContactDetails
{
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string LinkedIn { get; init; } = string.Empty;
    public string Website { get; init; } = string.Empty;
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
