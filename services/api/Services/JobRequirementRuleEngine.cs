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
        @"\b(?:work\s+authori[sz]ation|authori[sz]ed\s+to\s+work|right\s+to\s+work|work\s+permit|visa|sponsorship|residence\s+permit|eligible\s+to\s+work|legally\s+entitled\s+to\s+work)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExplicitWorkAuthorizationCue = new(
        @"\b(?:must\s+(?:already\s+)?(?:be\s+)?(?:legally\s+)?(?:authori[sz]ed|eligible)|(?:current|valid)\s+right\s+to\s+work|legal\s+right\s+to\s+work|legally\s+entitled\s+to\s+work|work\s+authori[sz]ation\s+(?:is\s+)?required|must\s+have\s+(?:a\s+)?(?:valid\s+)?work\s+permit|valid\s+work\s+permit\s+required)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NoSponsorshipCue = new(
        @"\b(?:no|without|cannot|can't|do\s+not|don't|will\s+not|won't|unable\s+to|not\s+able\s+to)\s+(?:(?:provide|offer)\s+)?(?:(?:visa|employer|work)\s+)?sponsorship\b|\bsponsorship\s+(?:is\s+)?not\s+(?:available|provided|offered)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SponsorshipOfferedCue = new(
        @"\b(?:visa\s+sponsorship\s+(?:is\s+)?(?:available|provided|offered)|sponsorship\s+(?:is\s+)?available|we\s+(?:can|will|do)\s+sponsor|offer\s+(?:visa\s+)?sponsorship)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CandidateNeedsSponsorship = new(
        @"\b(?:require|requires|requiring|need|needs)\s+(?:(?:an?|employer|visa|work)\s+)?sponsorship\b|\bsponsorship\s+required\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExplicitlyUnauthorised = new(
        @"\b(?:not\s+(?:currently\s+)?authori[sz]ed|not\s+eligible\s+to\s+work|no\s+legal\s+right\s+to\s+work|does\s+not\s+have\s+(?:the\s+)?(?:right\s+to\s+work|work\s+authori[sz]ation)|no\s+(?:valid\s+)?work\s+permit|not\s+allowed\s+to\s+work)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CertificationTopic = new(
        @"\b(?:certifications?|certificates?|certified|licen[cs]es?|forklift|driver'?s?\s+licen[cs]e|driving\s+licen[cs]e|security\s+clearance|clearance)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CredentialRequirementCue = new(
        @"\b(?:required|mandatory|essential|must|must-have|must\s+hold|must\s+possess|condition\s+of\s+employment|is\s+a\s+requirement)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExplicitlyNotRequiredCue = new(
        @"\b(?:not\s+required|not\s+mandatory|no\s+(?:certificate|certification|licen[cs]e)\s+required)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NamedCredential = new(
        @"\b(?:AWS\s+Certified\s+Developer(?:\s*[-–]\s*Associate)?|AWS\s+Solutions?\s+Architect(?:\s*[-–]\s*Associate)?|Microsoft\s+Certified(?:\s+[A-Za-z0-9.+#-]+){0,4}|CompTIA(?:\s+[A-Za-z0-9.+#-]+){0,3}|PRINCE2|PMP|CISSP|CISM|CCNA|CCNP|CKA|CKAD|forklift(?:\s+operator)?\s+licen[cs]e|driver'?s?\s+licen[cs]e|driving\s+licen[cs]e|security\s+clearance)\b(?:\s+(?:certification|certificate))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StudentRoleCue = new(
        @"\b(?:werkstudent(?:in)?|working\s+student)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StudentEnrollmentCue = new(
        @"\b(?:currently\s+enrolled|must\s+be\s+enrolled|enrolled\s+(?:at|in)\s+(?:a\s+)?(?:university|college|higher\s+education\s+institution)|active\s+student\s+status|only\s+(?:current\s+)?students|current\s+university\s+student)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StudentEnrollmentEntryTopic = new(
        @"\b(?:werkstudent(?:in)?|working\s+student|currently\s+enrolled|must\s+be\s+enrolled|enrolled|enrolment|enrollment|student\s+status|current\s+student|university\s+student|currently\s+studying|still\s+studying)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StudentEnrollmentProfileCue = new(
        @"\b(?:in\s+progress|ongoing|currently\s+studying|currently\s+enrolled|enrolled|expected\s+graduation|expected\s+completion)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExplicitlyNotEnrolledCue = new(
        @"\b(?:not\s+currently\s+enrolled|not\s+enrolled|not\s+currently\s+studying|no\s+longer\s+enrolled)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExperienceAmount = new(
        @"(?<amount>\d+(?:\.\d+)?|one|two|three|four|five|six|seven|eight|nine|ten)\s*\+?\s*(?<unit>years?|yrs?|months?)\s+(?:of\s+)?(?:(?:relevant|professional|commercial|practical|work|software)\s+)*(?:experience)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExperienceDurationQuestion = new(
        @"\b(?:years?|yrs?|months?)\b.{0,60}\bexperience\b|\bexperience\b.{0,60}\b(?:years?|yrs?|months?)\b|\bactual\s+start\s+and\s+end\s+dates?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SummaryExperienceAmount = new(
        @"(?<amount>\d+(?:\.\d+)?)\s*\+?\s*(?<unit>years?|yrs?|months?)\s+(?:of\s+)?(?:(?:professional|relevant|commercial|software|work)\s+)*(?:experience)\b|\bexperience\b.{0,30}\b(?<reverseAmount>\d+(?:\.\d+)?)\s*(?<reverseUnit>years?|yrs?|months?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PeriodMonthYear = new(
        @"\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+20\d{2}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExperienceThresholdCue = new(
        @"\b(?:minimum(?:\s+of)?|at\s+least|no\s+less\s+than|required|mandatory|must\s+have|essential)\b",
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
        ApplyWorkAuthorizationRequirement(vacancyText, profile, result);
        ApplyMinimumExperienceRequirement(vacancyText, profile, result);
        ApplyCredentialRequirements(vacancyText, profile, result);
        ApplyStudentEnrollmentRequirement(request, vacancyText, profile, result);
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

    private static void ApplyWorkAuthorizationRequirement(
        string vacancyText,
        CandidateProfile profile,
        JobAnalysisResult result)
    {
        var clauses = SplitClauses(vacancyText);
        var hasExplicitNoSponsorship = clauses.Any(clause =>
            WorkAuthorizationTopic.IsMatch(clause) && NoSponsorshipCue.IsMatch(clause));
        var hasExplicitRightToWorkGate = clauses.Any(clause =>
            WorkAuthorizationTopic.IsMatch(clause) && ExplicitWorkAuthorizationCue.IsMatch(clause));
        var sponsorshipOfferOnly = clauses.Any(clause => SponsorshipOfferedCue.IsMatch(clause)) &&
            !hasExplicitNoSponsorship && !hasExplicitRightToWorkGate;
        var authorizationRequirement = clauses
            .Where(clause => NoSponsorshipCue.IsMatch(clause) || ExplicitWorkAuthorizationCue.IsMatch(clause))
            .Select(clause => WorkAuthorizationTopic.Match(clause))
            .Where(match => match.Success)
            .Select(match => match.Value.Trim())
            .FirstOrDefault() ?? "work authorization";

        RemoveWorkAuthorizationEntries(result);

        // Mentioning visa support alone is not a disqualifier. Enforce this rule only when the
        // vacancy explicitly requires existing work eligibility or says sponsorship is unavailable.
        if ((!hasExplicitRightToWorkGate && !hasExplicitNoSponsorship) || sponsorshipOfferOnly)
        {
            return;
        }

        var profileStatus = profile.WorkAuthorization?.Trim() ?? string.Empty;
        if (IsPlaceholder(profileStatus))
        {
            AddGap(
                result,
                authorizationRequirement,
                "Must-have",
                "Unverified",
                "The vacancy explicitly makes work eligibility or sponsorship status a condition, but the candidate profile does not establish the current status.");
            AddQuestionIfMissing(
                result,
                "work authorization",
                "Are you legally authorised to work in the role's country under the stated sponsorship conditions? Update WorkAuthorization in the candidate profile with an accurate, country-specific answer.");
            result.Summary = AppendText(result.Summary,
                "Work eligibility is a mandatory screening condition, but the candidate profile does not establish the current status.");
            result.Rationale = AppendText(result.Rationale,
                "Work eligibility remains unverified and requires confirmation; it has not been assumed from nationality, location, or the vacancy.");
            return;
        }

        var requiresSponsorship = CandidateNeedsSponsorship.IsMatch(profileStatus);
        if ((hasExplicitNoSponsorship && requiresSponsorship) ||
            ExplicitlyUnauthorised.IsMatch(profileStatus))
        {
            AddGap(
                result,
                authorizationRequirement,
                "Must-have",
                "Unmet",
                "The profile explicitly indicates that the stated work-authorisation or no-sponsorship condition is not met.");
            result.Summary = AppendText(result.Summary,
                "The candidate profile explicitly conflicts with the vacancy's mandatory work-eligibility condition.");
            result.Rationale = AppendText(result.Rationale,
                "The right-to-work/sponsorship condition is explicitly unmet; the recommendation should not be Apply.");
            return;
        }

        var limitedStudentPermit = Regex.IsMatch(
            profileStatus,
            @"\b(?:student\s+(?:visa|residence\s+permit)|limited\s+working\s+hours|work\s+limits?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var statesPositiveEligibility = Regex.IsMatch(
            profileStatus,
            @"\b(?:authori[sz]ed|eligible|legal\s+right|right\s+to\s+work|valid\s+work\s+permit)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            !ExplicitlyUnauthorised.IsMatch(profileStatus) &&
            !(limitedStudentPermit && !Regex.IsMatch(
                profileStatus,
                @"\b(?:eligible\s+for\s+this\s+role|meets?\s+the\s+role'?s?\s+working[-\s]+hour\s+requirements)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

        if (statesPositiveEligibility && !requiresSponsorship)
        {
            result.MatchedRequirements.Add(new MatchedRequirement
            {
                Requirement = authorizationRequirement,
                Evidence = $"Candidate profile explicitly states: '{profileStatus}'."
            });
            result.Summary = AppendText(result.Summary,
                "The candidate profile states work eligibility consistent with the vacancy's stated sponsorship condition.");
            result.Rationale = AppendText(result.Rationale,
                "Work eligibility was matched from the candidate's explicit profile statement, not inferred from nationality or residence.");
            return;
        }

        AddGap(
            result,
            authorizationRequirement,
            "Must-have",
            "Unverified",
            "The profile contains a work-status statement, but it does not establish eligibility for this specific country and sponsorship condition.");
        AddQuestionIfMissing(
            result,
            "work authorization",
            "Please confirm whether you meet the role's country-specific right-to-work and sponsorship conditions; update WorkAuthorization with the exact verified status.");
        result.Summary = AppendText(result.Summary,
            "The mandatory work-eligibility condition remains unverified from the current profile wording.");
        result.Rationale = AppendText(result.Rationale,
            "The work-eligibility condition requires confirmation because the profile wording does not establish the specific requirement.");
    }

    private static void RemoveWorkAuthorizationEntries(JobAnalysisResult result)
    {
        result.MatchedRequirements.RemoveAll(item =>
            item is not null && WorkAuthorizationTopic.IsMatch(item.Requirement ?? string.Empty));
        result.Gaps.RemoveAll(item =>
            item is not null && WorkAuthorizationTopic.IsMatch(item.Requirement ?? string.Empty));
        result.QuestionsToVerify.RemoveAll(question =>
            WorkAuthorizationTopic.IsMatch(question ?? string.Empty));
        result.Summary = RemoveSentencesMentioningWorkAuthorization(result.Summary);
        result.Rationale = RemoveSentencesMentioningWorkAuthorization(result.Rationale);
    }

    private static void ApplyMinimumExperienceRequirement(
        string vacancyText,
        CandidateProfile profile,
        JobAnalysisResult result)
    {
        var requirements = SplitClauses(vacancyText)
            .SelectMany(clause => ExperienceAmount.Matches(clause).Select(match => new
            {
                Clause = clause,
                Match = match,
                Months = ParseAmount(match.Groups["amount"].Value) *
                         (match.Groups["unit"].Value.StartsWith("month", StringComparison.OrdinalIgnoreCase)
                            ? 1
                            : 12),
                Preferred = PreferredCue.IsMatch(clause) &&
                            !Regex.IsMatch(
                                clause,
                                @"\b(?:not\s+required|not\s+mandatory)\b",
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                Mandatory = ExperienceThresholdCue.IsMatch(clause) ||
                            LanguageRequirementCue.IsMatch(clause)
            }))
            .Where(item => item.Months > 0 && (item.Preferred || item.Mandatory))
            .GroupBy(item => $"{item.Months}:{item.Preferred}")
            .Select(group => group.First())
            .OrderByDescending(item => item.Months)
            .ToList();

        if (requirements.Count == 0)
        {
            return;
        }

        // Replace model-created duration gaps/questions with checks tied to the explicit threshold.
        result.Gaps.RemoveAll(gap =>
            gap is not null && ExperienceDurationQuestion.IsMatch(gap.Requirement ?? string.Empty));
        result.QuestionsToVerify.RemoveAll(question => ExperienceDurationQuestion.IsMatch(question));
        result.MatchedRequirements.RemoveAll(item =>
            item is not null && ExperienceDurationQuestion.IsMatch(item.Requirement ?? string.Empty));

        var candidateMonths = GetCandidateExperienceMonths(profile);
        foreach (var requirement in requirements)
        {
            var label = requirement.Match.Value.Trim();
            var isMet = candidateMonths.HasValue && candidateMonths.Value >= requirement.Months;
            var severity = requirement.Preferred ? "Preferred" : "Must-have";

            if (isMet)
            {
                result.MatchedRequirements.Add(new MatchedRequirement
                {
                    Requirement = label,
                    Evidence = BuildExperienceEvidence(profile, candidateMonths!.Value)
                });
                continue;
            }

            var status = candidateMonths.HasValue ? "Unmet" : "Unverified";
            AddGap(
                result,
                label,
                severity,
                status,
                candidateMonths.HasValue
                    ? $"The profile indicates about {candidateMonths.Value / 12.0:0.0} years of professional experience, below the vacancy's stated threshold."
                    : "The vacancy states a minimum experience duration, but the profile does not establish a reliable total of relevant professional experience.");

            if (severity == "Must-have" && status == "Unverified")
            {
                AddQuestionIfMissing(
                    result,
                    "experience duration",
                    $"How much relevant professional experience do you have in total? The vacancy specifies '{label}'. Add accurate total experience or complete employment dates to the profile.");
            }
        }

        // Actual employment dates become relevant only for an explicit duration requirement.
        result.QuestionsToVerify.RemoveAll(question =>
            EmploymentMetadataQuestion.IsMatch(question) &&
            !question.Contains("relevant professional experience", StringComparison.OrdinalIgnoreCase));
    }

    private static double ParseAmount(string value)
    {
        if (double.TryParse(value, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric;
        }

        return value.ToLowerInvariant() switch
        {
            "one" => 1,
            "two" => 2,
            "three" => 3,
            "four" => 4,
            "five" => 5,
            "six" => 6,
            "seven" => 7,
            "eight" => 8,
            "nine" => 9,
            "ten" => 10,
            _ => 0
        };
    }

    private static int? GetCandidateExperienceMonths(CandidateProfile profile)
    {
        var summaryMatch = SummaryExperienceAmount.Match(profile.ProfessionalSummary ?? string.Empty);
        int? summaryMonths = null;
        if (summaryMatch.Success)
        {
            var amountText = summaryMatch.Groups["amount"].Success
                ? summaryMatch.Groups["amount"].Value
                : summaryMatch.Groups["reverseAmount"].Value;
            var unitText = summaryMatch.Groups["unit"].Success
                ? summaryMatch.Groups["unit"].Value
                : summaryMatch.Groups["reverseUnit"].Value;

            if (double.TryParse(amountText, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var amount))
            {
                summaryMonths = (int)Math.Round(
                    amount * (unitText.StartsWith("month", StringComparison.OrdinalIgnoreCase) ? 1 : 12),
                    MidpointRounding.AwayFromZero);
            }
        }

        var periodMonths = GetExperienceMonthsFromPeriods(profile.Experience);
        if (summaryMonths.HasValue && periodMonths.HasValue)
        {
            return Math.Max(summaryMonths.Value, periodMonths.Value);
        }

        return summaryMonths ?? periodMonths;
    }

    private static int? GetExperienceMonthsFromPeriods(IReadOnlyCollection<ExperienceEntry> experiences)
    {
        if (experiences.Count == 0 ||
            experiences.Any(experience => string.IsNullOrWhiteSpace(experience.Period) ||
                experience.Period.Contains("replace", StringComparison.OrdinalIgnoreCase) ||
                experience.Period.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
                experience.Period.Contains("not specified", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var intervals = new List<(int Start, int End)>();
        foreach (var experience in experiences)
        {
            var matches = PeriodMonthYear.Matches(experience.Period);
            DateTime start;
            DateTime end;

            if (matches.Count >= 2)
            {
                if (!TryParseMonthYear(matches[0].Value, out start) ||
                    !TryParseMonthYear(matches[matches.Count - 1].Value, out end))
                {
                    return null;
                }
            }
            else if (matches.Count == 1 &&
                     Regex.IsMatch(
                         experience.Period,
                         @"\b(?:present|current|now)\b",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                if (!TryParseMonthYear(matches[0].Value, out start))
                {
                    return null;
                }

                var now = DateTime.Now;
                end = new DateTime(now.Year, now.Month, 1);
            }
            else
            {
                return null;
            }

            if (end < start)
            {
                return null;
            }

            intervals.Add((start.Year * 12 + start.Month, end.Year * 12 + end.Month));
        }

        intervals.Sort((left, right) => left.Start.CompareTo(right.Start));
        var totalMonths = 0;
        var currentStart = intervals[0].Start;
        var currentEnd = intervals[0].End;

        foreach (var interval in intervals.Skip(1))
        {
            if (interval.Start <= currentEnd)
            {
                currentEnd = Math.Max(currentEnd, interval.End);
                continue;
            }

            totalMonths += currentEnd - currentStart;
            currentStart = interval.Start;
            currentEnd = interval.End;
        }

        totalMonths += currentEnd - currentStart;
        return totalMonths > 0 ? totalMonths : null;
    }

    private static bool TryParseMonthYear(string value, out DateTime date) =>
        DateTime.TryParseExact(
            value.Trim(),
            new[] { "MMM yyyy", "MMMM yyyy" },
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out date);

    private static string BuildExperienceEvidence(CandidateProfile profile, int months)
    {
        var summary = profile.ProfessionalSummary?.Trim() ?? string.Empty;
        if (SummaryExperienceAmount.IsMatch(summary))
        {
            return $"Candidate professional summary states: '{summary}'.";
        }

        return $"The dated entries in the candidate profile establish approximately {months / 12.0:0.0} years of professional experience.";
    }

    private static void ApplyCredentialRequirements(
        string vacancyText,
        CandidateProfile profile,
        JobAnalysisResult result)
    {
        foreach (var clause in SplitClauses(vacancyText))
        {
            var topic = CertificationTopic.Match(clause);
            if (!topic.Success)
            {
                continue;
            }

            // Language-level certificates are handled by the deterministic language rule.
            if (Regex.IsMatch(
                    clause,
                    @"\b(?:German|Deutsch|English|Englisch|language|CEFR|language\s+certificate)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                !Regex.IsMatch(
                    clause,
                    @"\b(?:forklift|driver'?s?\s+licen[cs]e|driving\s+licen[cs]e|security\s+clearance)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                continue;
            }

            var isPreferred = PreferredCue.IsMatch(clause) &&
                !Regex.IsMatch(
                    clause,
                    @"\b(?:not\s+required|not\s+mandatory)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var isMandatory = !isPreferred && CredentialRequirementCue.IsMatch(clause);
            if (!isPreferred && !isMandatory)
            {
                continue;
            }

            if (!isPreferred && ExplicitlyNotRequiredCue.IsMatch(clause) &&
                !Regex.IsMatch(
                    clause,
                    @"\b(?:preferred|advantage|a\s+plus|nice\s+to\s+have)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                continue;
            }

            var credentialLabelMatch = NamedCredential.Match(clause);
            var credentialLabel = credentialLabelMatch.Success
                ? credentialLabelMatch.Value.Trim()
                : clause.Trim().Trim(':', '-', '–', ',');

            var requestedCredentialTokens = CredentialTokens(credentialLabel);
            bool RefersToSameCredential(string? existing)
            {
                if (!CertificationTopic.IsMatch(existing ?? string.Empty))
                {
                    return false;
                }

                var existingTokens = CredentialTokens(existing);
                return requestedCredentialTokens.Count == 0 ||
                       requestedCredentialTokens.Intersect(existingTokens, StringComparer.OrdinalIgnoreCase).Any();
            }

            result.MatchedRequirements.RemoveAll(item =>
                item is not null && RefersToSameCredential(item.Requirement));
            result.Gaps.RemoveAll(gap =>
                gap is not null && RefersToSameCredential(gap.Requirement));
            result.QuestionsToVerify.RemoveAll(question => RefersToSameCredential(question));

            var evidence = profile.Certifications?.FirstOrDefault(certification =>
                !IsPlaceholder(certification ?? string.Empty) &&
                CredentialTokens(credentialLabel).Count > 0 &&
                CredentialTokens(credentialLabel).All(token =>
                    CredentialTokens(certification).Contains(token, StringComparer.OrdinalIgnoreCase)));

            if (!string.IsNullOrWhiteSpace(evidence))
            {
                result.MatchedRequirements.Add(new MatchedRequirement
                {
                    Requirement = credentialLabel,
                    Evidence = $"Candidate profile lists the credential: '{evidence.Trim()}'."
                });
                continue;
            }

            var severity = isMandatory ? "Must-have" : "Preferred";
            AddGap(
                result,
                credentialLabel,
                severity,
                "Unverified",
                $"The vacancy states this credential is {severity.ToLowerInvariant()}, but the candidate profile does not confirm whether it is held and valid.");

            if (isMandatory)
            {
                AddQuestionIfMissing(
                    result,
                    credentialLabel,
                    $"Do you hold a currently valid '{credentialLabel}'? Add the exact credential name and validity details to Certifications if held.");
            }
        }
    }

    private static List<string> CredentialTokens(string? value)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "is", "are", "and", "or", "of", "to", "for", "with", "must",
            "hold", "held", "have", "has", "required", "mandatory", "essential", "preferred",
            "valid", "currently", "candidate", "applicant", "applicants", "role", "position",
            "condition", "employment", "certificate", "certification", "certified", "license",
            "licence", "licenses", "licences", "clearance", "this", "that", "relevant", "all"
        };

        return Regex.Matches(value ?? string.Empty, @"[a-z0-9+#.]+", RegexOptions.IgnoreCase)
            .Select(match => match.Value.Trim('.'))
            .Where(token => token.Length > 0 && !ignored.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ApplyStudentEnrollmentRequirement(
        JobAnalysisRequest request,
        string vacancyText,
        CandidateProfile profile,
        JobAnalysisResult result)
    {
        var clauses = SplitClauses(vacancyText);
        var titleMatch = StudentRoleCue.Match(request.JobTitle ?? string.Empty);
        var enrollmentClause = clauses.FirstOrDefault(clause => StudentEnrollmentCue.IsMatch(clause));
        if (!titleMatch.Success && enrollmentClause is null)
        {
            return;
        }

        var enrollmentMatch = enrollmentClause is null
            ? titleMatch
            : StudentEnrollmentCue.Match(enrollmentClause);
        var label = enrollmentMatch.Success
            ? enrollmentMatch.Value.Trim()
            : titleMatch.Value.Trim();

        result.MatchedRequirements.RemoveAll(item =>
            item is not null && StudentEnrollmentEntryTopic.IsMatch(item.Requirement ?? string.Empty));
        result.Gaps.RemoveAll(gap =>
            gap is not null && StudentEnrollmentEntryTopic.IsMatch(gap.Requirement ?? string.Empty));
        result.QuestionsToVerify.RemoveAll(question => StudentEnrollmentEntryTopic.IsMatch(question ?? string.Empty));

        var educationEvidence = profile.Education?.FirstOrDefault(education =>
            !IsPlaceholder(education ?? string.Empty) &&
            StudentEnrollmentProfileCue.IsMatch(education) &&
            !ExplicitlyNotEnrolledCue.IsMatch(education));

        var explicitlyNotEnrolled = (profile.Education ?? [])
            .Concat(profile.Constraints ?? [])
            .Any(entry => ExplicitlyNotEnrolledCue.IsMatch(entry ?? string.Empty));

        if (!string.IsNullOrWhiteSpace(educationEvidence))
        {
            result.MatchedRequirements.Add(new MatchedRequirement
            {
                Requirement = label,
                Evidence = $"Candidate profile lists current study/enrolment: '{educationEvidence.Trim()}'."
            });
            result.Summary = AppendText(result.Summary,
                "Current university study/enrolment is supported by the candidate profile.");
            result.Rationale = AppendText(result.Rationale,
                "The student-status condition was checked against the candidate's education entry.");
            return;
        }

        AddGap(
            result,
            label,
            "Must-have",
            explicitlyNotEnrolled ? "Unmet" : "Unverified",
            explicitlyNotEnrolled
                ? "The candidate profile explicitly states that the candidate is not currently enrolled."
                : "The role requires current student status, but the candidate profile does not establish current enrolment.");

        if (!explicitlyNotEnrolled)
        {
            AddQuestionIfMissing(
                result,
                "student enrollment",
                "Are you currently enrolled at a university for the period required by this role? Add the accurate current-study status to Education.");
        }
    }

    private static string[] SplitClauses(string text) =>
        Regex.Split(text ?? string.Empty, @"(?<=[.!?;])\s+|\r?\n")
            .Select(clause => clause.Trim())
            .Where(clause => !string.IsNullOrWhiteSpace(clause))
            .ToArray();

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
