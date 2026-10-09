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

    private static readonly Regex ExplicitExperienceDuration = new(
        @"\b(?:\d+\+?|one|two|three|four|five|six|seven|eight|nine|ten|several|multiple)\s+(?:years?|months?)(?:['’]s?)?\s+(?:of\s+)?(?:(?:work|relevant|professional|commercial|practical)\s+)?experience\b|\bexperience\b.{0,45}\b(?:\d+\+?|one|two|three|four|five|six|seven|eight|nine|ten|several|multiple)\s+(?:years?|months?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EmploymentMetadataGap = new(
        @"\b(?:employment\s+(?:dates?|duration)|exact\s+(?:employment\s+)?dates?|work\s+history\s+dates?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EmploymentMetadataQuestion = new(
        @"\b(?:(?:please\s+)?(?:provide|confirm|share|enter|specify)\s+(?:the\s+)?(?:actual\s+)?(?:start\s+and\s+end\s+dates?|employment\s+dates?|professional\s+roles?\s+(?:start\s+and\s+end\s+)?dates?)|(?:actual\s+)?start\s+and\s+end\s+dates?|employment\s+(?:(?:start|end)\s+)?dates?|exact\s+(?:employment\s+)?dates?|dates?\s+for\s+(?:the\s+)?[\w.-]+\s+roles?|experience\s+duration|duration\s+of\s+(?:your\s+)?(?:professional|work|employment)\s+experience)\b|\b(?:to\s+verify|verify|confirm)\s+(?:your\s+)?(?:experience\s+duration|employment\s+dates?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WorkAuthorizationTopic = new(
        @"\b(?:work\s+authori[sz]ation|right\s+to\s+work|work\s+permit|visa|sponsorship|residence\s+permit|eligible\s+to\s+work|legally\s+entitled\s+to\s+work)\b",
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
        ApplyVacancyRelevanceRules(vacancyText, result);

        if (string.IsNullOrWhiteSpace(result.Summary))
        {
            result.Summary = "The fit summary is based on profile-supported matches and requirements explicitly stated in the vacancy.";
        }

        if (string.IsNullOrWhiteSpace(result.Rationale))
        {
            result.Rationale = "The rationale is based on the candidate profile and the vacancy requirements; unstated language requirements were not scored.";
        }
    }

    private static void ApplyVacancyRelevanceRules(
        string vacancyText,
        JobAnalysisResult result)
    {
        // Missing employment dates are not a job-fit gap unless the vacancy makes a duration
        // requirement explicit. Keep this independent from other eligibility checks.
        if (!ExplicitExperienceDuration.IsMatch(vacancyText))
        {
            result.Gaps.RemoveAll(gap =>
                gap is not null && EmploymentMetadataGap.IsMatch(gap.Requirement ?? string.Empty));

            result.QuestionsToVerify.RemoveAll(question =>
                EmploymentMetadataQuestion.IsMatch(question));

            result.Summary = RemoveIrrelevantEmploymentMetadataSentences(result.Summary);
            result.Rationale = RemoveIrrelevantEmploymentMetadataSentences(result.Rationale);
        }

        // Do not invent work-authorisation checks. They are relevant only when the vacancy
        // mentions visas, sponsorship, permits, or an explicit right-to-work condition.
        if (!WorkAuthorizationTopic.IsMatch(vacancyText))
        {
            result.Gaps.RemoveAll(gap =>
                gap is not null && WorkAuthorizationTopic.IsMatch(gap.Requirement ?? string.Empty));

            result.QuestionsToVerify.RemoveAll(question =>
                WorkAuthorizationTopic.IsMatch(question));

            result.Summary = RemoveSentencesMentioningWorkAuthorization(result.Summary);
            result.Rationale = RemoveSentencesMentioningWorkAuthorization(result.Rationale);
        }
    }

    private static string RemoveSentencesMentioningWorkAuthorization(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
        return string.Join(" ", sentences.Where(sentence =>
            !WorkAuthorizationTopic.IsMatch(sentence))).Trim();
    }

    private static string RemoveIrrelevantEmploymentMetadataSentences(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
        return string.Join(" ", sentences.Where(sentence =>
        {
            var mentionsDatesOrDuration = EmploymentMetadataGap.IsMatch(sentence) ||
                Regex.IsMatch(
                    sentence,
                    @"\b(?:dates?|duration)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var indicatesUnknown = Regex.IsMatch(
                sentence,
                @"\b(?:unknown|unverified|placeholder|gap|verify|verified|missing|not specified)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            var asksToVerifyMetadata = EmploymentMetadataQuestion.IsMatch(sentence);
            return !(asksToVerifyMetadata || (mentionsDatesOrDuration && indicatesUnknown));
        })).Trim();
    }

    private static void ApplyLanguage(
        string vacancyText,
        CandidateProfile profile,
        JobAnalysisResult result,
        string language,
        string alternateName)
    {
        RemoveLanguageNarrative(result, language, alternateName);
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
        var learning = ContainsWord(proficiency, "Learning");
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
                    $"{language} proficiency does not meet the vacancy's explicitly stated {GetRequiredLevelLabel(nearbyText) ?? "mandatory"} requirement based on the candidate profile.");
                RemoveLanguageQuestions(result, language, alternateName);
                result.Summary = AppendText(result.Summary,
                    $"{language} is a mandatory requirement and the candidate profile indicates the required level is not met.");
                result.Rationale = AppendText(result.Rationale,
                    $"{language} is a mandatory requirement explicitly contradicted by the candidate profile; the recommendation should not be Apply.");
                return;
            }

            if (isUnknown || learning || (requiredLevel.HasValue && !profileLevel.HasValue))
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
                result.Summary = AppendText(result.Summary,
                    $"{language} is a mandatory requirement, but the candidate profile does not establish a verified proficiency level.");
                result.Rationale = AppendText(result.Rationale,
                    $"{language} proficiency remains unverified, so the recommendation must be Review until the requirement is confirmed.");
                return;
            }

            // The profile states a level, and there is no evidence it falls below any stated target.
            result.MatchedRequirements.Add(new MatchedRequirement
            {
                Requirement = $"{language} proficiency",
                Evidence = $"Candidate profile lists {language} proficiency as '{proficiency}'."
            });
            result.Summary = AppendText(result.Summary,
                $"{language} proficiency is supported by the candidate profile.");
            result.Rationale = AppendText(result.Rationale,
                $"{language} proficiency was matched using the candidate profile, not inferred from the job description.");
            RemoveLanguageQuestions(result, language, alternateName);
            return;
        }

        // A stated preferred language is scored as a preferred gap if the profile says learning,
        // is blank, or otherwise does not establish proficiency. It does not block Apply.
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
            result.Summary = AppendText(result.Summary,
                $"{language} is preferred, and the candidate profile does not show verified working proficiency.");
            result.Rationale = AppendText(result.Rationale,
                $"{language} remains a preferred-skill gap; it does not block the application by itself.");
            RemoveLanguageQuestions(result, language, alternateName);
            return;
        }

        result.MatchedRequirements.Add(new MatchedRequirement
        {
            Requirement = $"{language} proficiency (preferred)",
            Evidence = $"Candidate profile lists {language} proficiency as '{proficiency}'."
        });
        result.Summary = AppendText(result.Summary,
            $"The preferred {language} requirement is supported by the candidate profile.");
        RemoveLanguageQuestions(result, language, alternateName);
    }

    private static string GetLanguageContext(string text, string language, string alternateName)
    {
        // Split clauses so a preference for German cannot accidentally classify English as preferred.
        var clauses = text.Split(new[] { '.', ';', '!', '?', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(" ", clauses.Where(clause =>
            ContainsWord(clause, language) || ContainsWord(clause, alternateName)));
    }

    private static int? GetRequiredLevel(string context)
    {
        // Highest level named as a condition in the vacancy (e.g. German C2 required).
        var match = LevelPattern.Match(context);
        return match.Success ? LevelRank(match.Value) : null;
    }

    private static string? GetRequiredLevelLabel(string context)
    {
        var match = LevelPattern.Match(context);
        return match.Success ? match.Value.ToUpperInvariant() : null;
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

    private static void RemoveLanguageNarrative(
        JobAnalysisResult result,
        string language,
        string alternateName)
    {
        result.Summary = RemoveSentencesMentioning(result.Summary, language, alternateName);
        result.Rationale = RemoveSentencesMentioning(result.Rationale, language, alternateName);
    }

    private static string RemoveSentencesMentioning(string text, string language, string alternateName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
        return string.Join(" ", sentences.Where(sentence =>
            !ContainsWord(sentence, language) && !ContainsWord(sentence, alternateName))).Trim();
    }

    private static string AppendText(string existing, string addition)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return addition;
        }

        if (existing.Contains(addition, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return $"{existing.TrimEnd()} {addition}";
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
