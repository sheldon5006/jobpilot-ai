using JobPilot.Api.Models;

namespace JobPilot.Api.Services;

public sealed record ProfileEvidenceFact(string Id, string Category, string Source, string Text);

/// <summary>
/// Builds stable, request-local identifiers for verified candidate-profile facts.
/// IDs are derived from the fact's category and position and are not persisted.
/// </summary>
public static class ProfileEvidenceCatalog
{
    public static IReadOnlyList<ProfileEvidenceFact> Create(CandidateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var facts = new List<ProfileEvidenceFact>();
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);

        void Add(string prefix, string category, string source, string? text)
        {
            var value = text?.Trim() ?? string.Empty;
            if (IsPlaceholder(value) || value.Length == 0)
            {
                return;
            }

            counters.TryGetValue(prefix, out var index);
            index++;
            counters[prefix] = index;
            facts.Add(new ProfileEvidenceFact(
                $"{prefix}-{index:000}",
                category,
                source,
                value));
        }

        Add("SUM", "professional_summary", "Professional summary", profile.ProfessionalSummary);

        foreach (var skill in profile.ProfessionalSkills ?? [])
        {
            Add("SKL", "professional_skill", "Professional skills", skill);
        }

        foreach (var skill in profile.ProjectAndAcademicSkills ?? [])
        {
            Add("PRJ", "project_academic_skill", "Project and academic skills", skill);
        }

        foreach (var experience in profile.Experience ?? [])
        {
            if (experience is null)
            {
                continue;
            }

            var isInternship = (experience.Role ?? string.Empty).Contains("intern", StringComparison.OrdinalIgnoreCase);
            var category = isInternship ? "internship_experience" : "professional_experience";
            var prefix = isInternship ? "INT" : "EXP";
            var roleLabel = string.IsNullOrWhiteSpace(experience.Role)
                ? "Experience"
                : experience.Role.Trim();
            var source = $"Experience — {roleLabel}";

            Add(
                "DATE",
                category,
                $"Experience dates — {roleLabel}",
                string.IsNullOrWhiteSpace(experience.Period)
                    ? string.Empty
                    : $"{roleLabel}: {experience.Period.Trim()}");

            foreach (var item in experience.Evidence ?? [])
            {
                Add(prefix, category, source, item);
            }
        }

        foreach (var education in profile.Education ?? [])
        {
            Add("EDU", "education", "Education", education);
        }

        foreach (var certification in profile.Certifications ?? [])
        {
            Add("CER", "certification", "Certifications", certification);
        }

        foreach (var language in profile.Languages ?? [])
        {
            if (language is null || string.IsNullOrWhiteSpace(language.Language))
            {
                continue;
            }

            Add(
                "LAN",
                "language",
                "Languages",
                $"{language.Language.Trim()}: {language.Proficiency?.Trim()}");
        }

        Add("AUTH", "work_authorization", "Work authorization", profile.WorkAuthorization);

        return facts;
    }

    private static bool IsPlaceholder(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("replace", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("not specified", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("not provided", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("tbd", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("n/a", StringComparison.OrdinalIgnoreCase);
}
