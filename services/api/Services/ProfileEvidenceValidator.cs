using System.Text.RegularExpressions;
using JobPilot.Api.Models;

namespace JobPilot.Api.Services;

/// <summary>
/// Ensures model-generated matches cite real profile facts of an appropriate category.
/// Human-readable evidence is rendered from the cited facts, never trusted from the model.
/// </summary>
public static class ProfileEvidenceValidator
{
    private static readonly Regex WorkAuthorizationRequirement = new(
        @"\b(?:work[\s-]+authori[sz]ation|authori[sz]ation[\s-]+to[\s-]+work|authori[sz]ed[\s-]+to[\s-]+work|right[\s-]+to[\s-]+work|work[\s-]+permit|visa|sponsorship|residence[\s-]+permit|work[\s-]+eligibility)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StudentRequirement = new(
        @"\b(?:werkstudent|working[\s-]+student|student[\s-]+status|enrol(?:l)?ment|enrolled|university[\s-]+student)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CredentialRequirement = new(
        @"\b(?:certification|certificate|credential|licen[cs]e|security[\s-]+clearance)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EducationRequirement = new(
        @"\b(?:degree|qualification|graduate|graduation|university|education|academic|study|bachelor|master|MSc|BSc|PhD|diploma)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LanguageRequirement = new(
        @"\b(?:language|German|Deutsch|English|Englisch|CEFR|proficiency|C2|C1|B2|B1|A2|A1)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExplicitProfessionalExperience = new(
        @"\b(?:professional|commercial|work)\s+experience\b|\b(?:minimum|at\s+least)\s+\d+\s+(?:years?|months?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExperienceRequirement = new(
        @"\b(?:experience|experienced|practical|professional|commercial|hands[\s-]+on|worked\s+on)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void Apply(CandidateProfile profile, JobAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(result);

        var facts = ProfileEvidenceCatalog.Create(profile)
            .ToDictionary(fact => fact.Id, StringComparer.OrdinalIgnoreCase);
        var validMatches = new List<MatchedRequirement>();
        var warnings = new List<string>();

        foreach (var match in result.MatchedRequirements ?? [])
        {
            if (match is null || string.IsNullOrWhiteSpace(match.Requirement))
            {
                warnings.Add("A model-generated match had no requirement label and was removed.");
                continue;
            }

            var suppliedIds = (match.EvidenceIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var citedFacts = suppliedIds
                .Where(facts.ContainsKey)
                .Select(id => facts[id])
                .ToList();

            if (suppliedIds.Count == 0 || citedFacts.Count == 0)
            {
                warnings.Add($"'{match.Requirement.Trim()}' was removed because it cited no valid profile fact.");
                continue;
            }

            if (citedFacts.Count != suppliedIds.Count)
            {
                warnings.Add($"'{match.Requirement.Trim()}' included one or more unknown profile-fact IDs.");
            }

            var allowedCategories = GetAllowedCategories(match.Requirement);
            var relevantFacts = citedFacts
                .Where(fact => allowedCategories.Contains(fact.Category))
                .ToList();

            if (relevantFacts.Count == 0)
            {
                warnings.Add($"'{match.Requirement.Trim()}' was removed because its cited facts do not establish that type of requirement.");
                continue;
            }

            match.EvidenceIds = relevantFacts.Select(fact => fact.Id).ToList();
            match.Evidence = string.Join(" ", relevantFacts.Select(fact => fact.Text));
            validMatches.Add(match);
        }

        result.MatchedRequirements = validMatches;
        result.EvidenceValidationWarnings = warnings.Distinct(StringComparer.Ordinal).ToList();

        if (result.EvidenceValidationWarnings.Count > 0)
        {
            result.Recommendation = "Review";
            result.Rationale = AppendOnce(
                result.Rationale,
                "One or more AI-generated matches failed profile-fact validation and were removed or restricted. Review the evidence warnings before relying on the recommendation.");
        }
    }

    private static HashSet<string> GetAllowedCategories(string requirement)
    {
        if (WorkAuthorizationRequirement.IsMatch(requirement))
        {
            return Set("work_authorization");
        }

        if (StudentRequirement.IsMatch(requirement))
        {
            return Set("education");
        }

        if (CredentialRequirement.IsMatch(requirement))
        {
            return Set("certification");
        }

        if (LanguageRequirement.IsMatch(requirement))
        {
            return Set("language");
        }

        if (EducationRequirement.IsMatch(requirement))
        {
            return Set("education");
        }

        if (ExplicitProfessionalExperience.IsMatch(requirement))
        {
            return Set("professional_summary", "professional_experience");
        }

        if (ExperienceRequirement.IsMatch(requirement))
        {
            return Set("professional_summary", "professional_experience", "internship_experience");
        }

        return Set(
            "professional_summary",
            "professional_skill",
            "professional_experience",
            "internship_experience",
            "project_academic_skill");
    }

    private static HashSet<string> Set(params string[] categories) =>
        new(categories, StringComparer.OrdinalIgnoreCase);

    private static string AppendOnce(string? text, string addition) =>
        !string.IsNullOrWhiteSpace(text) && text.Contains(addition, StringComparison.OrdinalIgnoreCase)
            ? text
            : string.IsNullOrWhiteSpace(text) ? addition : $"{text.TrimEnd()} {addition}";
}
