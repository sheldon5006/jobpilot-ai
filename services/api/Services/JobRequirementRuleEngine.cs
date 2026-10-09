using System.Text.RegularExpressions;
using JobPilot.Api.Models;

namespace JobPilot.Api.Services;

/// <summary>
/// Applies deterministic checks for language requirements explicitly stated in the vacancy.
/// The job ad is never accepted as evidence of the candidate's proficiency.
/// </summary>
public static class JobRequirementRuleEngine
{
    private static readonly Regex LanguageRequirementCue = new(
        @"\b(required|mandatory|essential|must-have|must demonstrate|must already|strict mandatory)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PreferredCue = new(
        @"\b(preferred|a plus|plus for|advantage|nice to have|optional|not mandatory|not required)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LevelPattern = new(
        @"\b(A1|A2|B1|B2|C1|C2)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void Apply(
        JobAnalysisRequest request,
        CandidateProfile profile,
        JobAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(result);

        var vacancyText = $"{request.JobTitle}\n{request.JobDescription}";
        ApplyLanguage(vacancyText, profile, result, "English", "Englisch");
        ApplyLanguage(vacancyText, profile, result, "German", "Deutsch");
    }

    private static void ApplyLanguage(
        string vacancyText,
        CandidateProfile profile,
        JobAnalysisResult result,
        string language,
        string alternateName)
    {
        var mentioned = ContainsWord(vacancyText, language) || ContainsWord(vacancyText, alternateName);

        // Drop model-invented language requirements if the vacancy does not mention that language.
        if (!mentioned)
        {
            RemoveLanguageEntries(result, language, alternateName);
            return;
        }

        var nearbyText = GetLanguageContext(vacancyText, language, alternateName);
        var isPreferred = PreferredCue.IsMatch(nearbyText);
        var isRequired = !isPreferred && LanguageRequirementCue.IsMatch(nearbyText);

        // Mentioning a language without saying it is required or preferred is not enough to score it.
        if (!isPreferred && !isRequired)
        {
            RemoveLanguageEntries(result, language, alternateName);
            return;
        }

        RemoveLanguageEntries(result, language, alternateName);

        var profileEntry = profile.Languages?.FirstOrDefault(entry =>
            ContainsWord(entry.Language ?? string.Empty, language) ||
            ContainsWord(entry.Language ?? string.Empty, alternateName));

        var proficiency = profileEntry?.Proficiency?.Trim() ?? string.Empty;
        var isUnknown = IsPlaceholder(proficiency);
        var requiredLevel = GetRequiredLevel(nearbyText);
        var profileLevel = GetLevel(proficiency);
        var explicitLearningMismatch = isRequired &&
            requiredLevel.HasValue &&
            ContainsWord(proficiency, "Learning") &&
            Regex.IsMatch(
                vacancyText,
                @"\b(still learning|learning)\b.{0,80}\b(do not meet|does not meet|do not qualify|not eligible)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var knownBelowLevel = isRequired &&
            requiredLevel.HasValue &&
            profileLevel.HasValue &&
            profileLevel.Value < requiredLevel.Value;

        if (isRequired)
        {
            if (explicitLearningMismatch || knownBelowLevel)
            {
                AddGap(
                    result,
                    language,
                    "Must-have",
                    "Unmet",
                    $"{language} proficiency does not meet the vacancy's explicitly stated {requiredLevel?.ToString() ?? "mandatory"} requirement based on the candidate profile.");
                RemoveLanguageQuestions(result, language, alternateName);
                return;
            }

            if (isUnknown || (requiredLevel.HasValue && !profileLevel.HasValue))
            {
                AddGap(
                    result,
                    language,
                    "Must-have",
                    "Unverified",
                    $"The vacancy makes {language} a mandatory requirement, but the candidate profile does not establish a verified proficiency level.");
                AddQuestionIfMissing(
                    result,
                    language,
                    $"What is your current {language} proficiency level? Update the candidate profile with an accurate level.");
                return;
            }

            // The profile states a level, and there is no evidence it falls below any stated target.
            result.MatchedRequirements.Add(new MatchedRequirement
            {
                Requirement = $"{language} proficiency",
                Evidence = $"Candidate profile lists {language} proficiency as '{proficiency}'."
            });
            RemoveLanguageQuestions(result, language, alternateName);
            return;
        }

        // A stated preferred language is scored as a preferred gap if the profile says learning,
        // is blank, or otherwise does not establish proficiency. It does not block Apply.
        var learning = ContainsWord(proficiency, "Learning");
        if (isUnknown || learning)
        {
            AddGap(
                result,
                language,
                "Preferred",
                isUnknown ? "Unverified" : "Unmet",
                isUnknown
                    ? $"{language} is preferred in the vacancy, but proficiency is not established in the candidate profile."
                    : $"The candidate profile says {language} is being learned; the preferred language skill is not yet evidenced at a working proficiency level.");
            RemoveLanguageQuestions(result, language, alternateName);
            return;
        }

        result.MatchedRequirements.Add(new MatchedRequirement
        {
            Requirement = $"{language} proficiency (preferred)",
            Evidence = $"Candidate profile lists {language} proficiency as '{proficiency}'."
        });
        RemoveLanguageQuestions(result, language, alternateName);
    }

    private static string GetLanguageContext(string text, string language, string alternateName)
    {
        var matches = Regex.Matches(
            text,
            $@"\b(?:{Regex.Escape(language)}|{Regex.Escape(alternateName)})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (matches.Count == 0)
        {
            return string.Empty;
        }

        // Inspect a bounded window around every mention to interpret nearby requirement wording.
        return string.Join(
            " ",
            matches.Select(match =>
            {
                var start = Math.Max(0, match.Index - 80);
                var length = Math.Min(text.Length - start, match.Length + 160);
                return text.Substring(start, length);
            }));
    }

    private static int? GetRequiredLevel(string context)
    {
        // Highest level named as a condition in the vacancy (e.g. German C2 required).
        var match = LevelPattern.Match(context);
        return match.Success ? LevelRank(match.Value) : null;
    }

    private static int? GetLevel(string proficiency)
    {
        var match = LevelPattern.Match(proficiency);
        return match.Success ? LevelRank(match.Value) : null;
    }

    private static int LevelRank(string level) => level.ToUpperInvariant() switch
    {
        "A1" => 1,
        "A2" => 2,
        "B1" => 3,
        "B2" => 4,
        "C1" => 5,
        "C2" => 6,
        _ => 0
    };

    private static bool IsPlaceholder(string proficiency) =>
        string.IsNullOrWhiteSpace(proficiency) ||
        proficiency.Contains("replace", StringComparison.OrdinalIgnoreCase) ||
        proficiency.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
        proficiency.Contains("not specified", StringComparison.OrdinalIgnoreCase) ||
        proficiency.Contains("not provided", StringComparison.OrdinalIgnoreCase) ||
        proficiency.Equals("tbd", StringComparison.OrdinalIgnoreCase) ||
        proficiency.Equals("n/a", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsWord(string text, string word) =>
        Regex.IsMatch(
            text,
            $@"\b{Regex.Escape(word)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static void RemoveLanguageEntries(JobAnalysisResult result, string language, string alternateName)
    {
        result.MatchedRequirements.RemoveAll(item =>
            item is not null &&
            (item.Requirement.Contains(language, StringComparison.OrdinalIgnoreCase) ||
             item.Requirement.Contains(alternateName, StringComparison.OrdinalIgnoreCase)));

        result.Gaps.RemoveAll(item =>
            item is not null &&
            (item.Requirement.Contains(language, StringComparison.OrdinalIgnoreCase) ||
             item.Requirement.Contains(alternateName, StringComparison.OrdinalIgnoreCase)));
        RemoveLanguageQuestions(result, language, alternateName);
    }

    private static void RemoveLanguageQuestions(JobAnalysisResult result, string language, string alternateName)
    {
        result.QuestionsToVerify.RemoveAll(question =>
            question.Contains(language, StringComparison.OrdinalIgnoreCase) ||
            question.Contains(alternateName, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddGap(
        JobAnalysisResult result,
        string language,
        string severity,
        string status,
        string explanation)
    {
        result.Gaps.Add(new RequirementGap
        {
            Requirement = $"{language} proficiency",
            Severity = severity,
            Status = status,
            Explanation = explanation
        });
    }

    private static void AddQuestionIfMissing(JobAnalysisResult result, string language, string question)
    {
        if (!result.QuestionsToVerify.Any(existing =>
            existing.Contains(language, StringComparison.OrdinalIgnoreCase)))
        {
            result.QuestionsToVerify.Add(question);
        }
    }
}
