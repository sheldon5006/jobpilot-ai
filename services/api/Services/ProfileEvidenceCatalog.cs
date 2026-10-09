using System.Text.RegularExpressions;
using JobPilot.Api.Models;

namespace JobPilot.Api.Services;

public sealed record ProfileEvidenceFact(string Id, string Category, string Source, string Text);

/// <summary>
/// Builds stable, request-local identifiers for facts taken from the candidate profile.
/// Placeholder clauses are removed, while valid parts of a fact remain available.
/// IDs are not persisted and do not expose a database identifier.
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
            var value = CleanFactText(text);
            if (value.Length == 0)
            {
                return;
            }

            counters.TryGetValue(prefix, out var index);
            index++;
            counters[prefix] = index;
            facts.Add(new ProfileEvidenceFact($"{prefix}-{index:000}", category, source, value));
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
            var roleLabel = string.IsNullOrWhiteSpace(experience.Role) ? "Experience" : experience.Role.Trim();
            var source = $"Experience — {roleLabel}";

            Add(
                "DATE",
                category,
                $"Experience dates — {roleLabel}",
                string.IsNullOrWhiteSpace(experience.Period) ? string.Empty : $"{roleLabel}: {experience.Period.Trim()}");

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

            var verifiedProficiency = CleanFactText(language.Proficiency);
            if (verifiedProficiency.Length == 0)
            {
                continue;
            }

            Add("LAN", "language", "Languages", $"{language.Language.Trim()}: {verifiedProficiency}");
        }

        Add("AUTH", "work_authorization", "Work authorization", profile.WorkAuthorization);
        return facts;
    }

    public static string CleanFactText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var retainedClauses = text
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(clause => !IsPlaceholder(clause))
            .ToList();

        return string.Join("; ", retainedClauses);
    }

    public static bool IsPlaceholder(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return Regex.IsMatch(
            text.Trim(),
            @"^(?:replace\b|placeholder\b|unknown\b|not\s+specified\b|not\s+provided\b|add\b|enter\b|update\b|fill\s+in\b|tbd\b|n\s*/\s*a\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
