using System.Text.RegularExpressions;
using JobPilot.Api.Models;

namespace JobPilot.Api.Services;

/// <summary>
/// Removes AI-generated matches and gaps whose substantive requirement terms do not
/// map back to the actual vacancy. Deterministic rules run before this validator and
/// therefore contribute only requirements derived from explicit vacancy conditions.
/// </summary>
public static class VacancyRequirementValidator
{
    private static readonly Regex WorkAuthorizationTopic = new(
        @"\b(?:work[\s-]+authori[sz]ation|authori[sz]ation[\s-]+to[\s-]+work|authori[sz]ed[\s-]+to[\s-]+work|right[\s-]+to[\s-]+work|work[\s-]+permit|visa|sponsorship|residence[\s-]+permit|eligible[\s-]+to[\s-]+work|legally[\s-]+entitled[\s-]+to[\s-]+work|work[\s-]+eligibility)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StudentTopic = new(
        @"\b(?:werkstudent(?:in)?|working[\s-]+student|student[\s-]+status|enrol(?:l)?ment|enrolled|university[\s-]+student|currently[\s-]+studying)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CredentialTopic = new(
        @"\b(?:certification|certificate|certified|credential|licen[cs]e|security[\s-]+clearance|forklift[\s-]+licen[cs]e)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SecurityClearanceTopic = new(
        @"\bsecurity[\s-]+clearance\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TokenPattern = new(
        @"[a-z0-9+#.]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "of", "to", "for", "with", "as", "is", "are", "be",
        "been", "being", "must", "have", "has", "had", "that", "this", "these", "those",
        "in", "on", "by", "from", "their", "candidate", "profile", "evidence", "explicitly",
        "supported", "professional", "practical", "relevant", "commercial", "work", "working",
        "experience", "experienced", "using", "knowledge", "demonstrated", "hands", "required",
        "requirement", "requirements", "mandatory", "essential", "preferred", "optional",
        "minimum", "current", "currently", "valid", "unrestricted", "legal", "legally",
        "country", "position", "based", "status", "level", "levels", "skill", "skills",
        "develop", "development", "developing", "developed", "develops", "build", "building",
        "built", "builds", "provide", "provided", "please", "candidate's", "role"
    };

    private static readonly HashSet<string> WorkAuthorizationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "work", "authorization", "authorisation", "authorized", "authorised", "right", "visa",
        "sponsorship", "residence", "permit", "eligible", "eligibility", "legally", "entitled"
    };

    private static readonly HashSet<string> StudentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "werkstudent", "werkstudentin", "working", "student", "students", "status", "enrolment",
        "enrollment", "enrolled", "university", "studying", "study", "currently", "current"
    };

    public static void Apply(JobAnalysisRequest request, JobAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        var vacancy = $"{request.JobTitle}\n{request.JobDescription}";
        var vacancyTokens = GetTokens(vacancy);
        var warnings = new List<string>();

        result.MatchedRequirements = (result.MatchedRequirements ?? [])
            .Where(match =>
            {
                if (match is null || string.IsNullOrWhiteSpace(match.Requirement))
                {
                    warnings.Add("A matched requirement with no label was removed.");
                    return false;
                }

                if (IsGrounded(match.Requirement, vacancyTokens))
                {
                    return true;
                }

                warnings.Add($"Matched requirement '{match.Requirement.Trim()}' was removed because it could not be traced to the vacancy.");
                return false;
            })
            .ToList();

        result.Gaps = (result.Gaps ?? [])
            .Where(gap =>
            {
                if (gap is null || string.IsNullOrWhiteSpace(gap.Requirement))
                {
                    warnings.Add("A gap with no requirement label was removed.");
                    return false;
                }

                if (IsGrounded(gap.Requirement, vacancyTokens))
                {
                    return true;
                }

                warnings.Add($"Gap '{gap.Requirement.Trim()}' was removed because it could not be traced to the vacancy.");
                return false;
            })
            .ToList();

        result.RequirementValidationWarnings = warnings.Distinct(StringComparer.Ordinal).ToList();

        if (result.RequirementValidationWarnings.Count > 0)
        {
            result.Recommendation = "Review";
            result.Rationale = AppendOnce(
                result.Rationale,
                "One or more model-generated requirements could not be traced to the vacancy and were removed. Review the requirement-validation warnings before relying on the recommendation.");
        }
    }

    private static bool IsGrounded(string requirement, IReadOnlySet<string> vacancyTokens)
    {
        var requirementTokens = GetTokens(requirement);
        return requirementTokens.Count > 0 && requirementTokens.All(vacancyTokens.Contains);
    }

    private static HashSet<string> GetTokens(string text)
    {
        var isWorkAuthorization = WorkAuthorizationTopic.IsMatch(text);
        var isStudentRequirement = StudentTopic.IsMatch(text);
        var isCredentialRequirement = CredentialTopic.IsMatch(text);
        var isSecurityClearance = SecurityClearanceTopic.IsMatch(text);

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TokenPattern.Matches(text.ToLowerInvariant()))
        {
            var token = Normalize(match.Value);
            if (token.Length == 0 || GenericWords.Contains(token))
            {
                continue;
            }

            if (isWorkAuthorization && WorkAuthorizationWords.Contains(token))
            {
                continue;
            }

            if (isStudentRequirement && StudentWords.Contains(token))
            {
                continue;
            }

            if (isCredentialRequirement &&
                (token is "certification" or "certificate" or "certified" or "credential" or "licence" or "license"))
            {
                continue;
            }

            if (isSecurityClearance && (token is "security" or "clearance"))
            {
                continue;
            }

            tokens.Add(token);
        }

        if (isWorkAuthorization) tokens.Add("__topic_work_authorization");
        if (isStudentRequirement) tokens.Add("__topic_student_status");
        if (isCredentialRequirement) tokens.Add("__topic_credential");
        if (isSecurityClearance) tokens.Add("__topic_security_clearance");
        return tokens;
    }

    private static string Normalize(string token)
    {
        token = token.Trim('.').ToLowerInvariant();

        if (token is "german" or "deutsch" or "deutschkenntnisse") return "__language_german";
        if (token is "english" or "englisch" or "englischkenntnisse") return "__language_english";
        if (token is "apis") return "api";
        if (token is "development" or "developing" or "developed" or "develops") return "develop";
        if (token is "building" or "built" or "builds") return "build";
        if (token is "certification" or "certificate" or "certified" or "credential") return "credential";
        if (token is "licence") return "license";

        if (token.EndsWith("ies", StringComparison.Ordinal) && token.Length > 4)
        {
            return token[..^3] + "y";
        }

        if (token.EndsWith('s') && token.Length > 4 && !token.EndsWith("ss", StringComparison.Ordinal) &&
            !token.EndsWith("us", StringComparison.Ordinal) && !token.EndsWith("is", StringComparison.Ordinal))
        {
            return token[..^1];
        }

        return token;
    }

    private static string AppendOnce(string? text, string addition) =>
        !string.IsNullOrWhiteSpace(text) && text.Contains(addition, StringComparison.OrdinalIgnoreCase)
            ? text
            : string.IsNullOrWhiteSpace(text) ? addition : $"{text.TrimEnd()} {addition}";
}
