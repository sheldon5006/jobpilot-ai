using System.Text.RegularExpressions;
using JobPilot.Api.Models;

namespace JobPilot.Api.Services;

/// <summary>
/// Computes the fit score from the requirement-level evidence instead of a model-chosen number:
/// every requirement stated in the vacancy is weighted by importance and credited by the strength
/// of the profile evidence that supports it. Mandatory gaps cap the score.
/// This is a transparent heuristic, not a probability of getting hired.
/// </summary>
public static class JobFitScoreCalibrator
{
    public const int ApplyThreshold = 75;
    public const int SkipThreshold = 40;
    private const int UnverifiedMustHaveCap = ApplyThreshold - 1;
    private const int UnmetMustHaveCap = 35;

    private const double MustHaveWeight = 3;
    private const double PreferredWeight = 1;
    private const double UnknownWeight = 2;
    private const double UnverifiedGapCredit = 0.25;

    public static void Apply(JobAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var distinctGaps = (result.Gaps ?? [])
            .Where(gap => gap is not null && !string.IsNullOrWhiteSpace(gap.Requirement))
            .GroupBy(gap => NormalizeRequirement(gap.Requirement), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(gap => IsStatus(gap, "Unmet") ? 0 : 1).ThenBy(GapImportanceOrder).First())
            .ToList();
        var gapKeys = distinctGaps.Select(gap => NormalizeRequirement(gap.Requirement)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A requirement listed as both matched and a gap counts once, as the (more cautious) gap.
        var distinctMatches = (result.MatchedRequirements ?? [])
            .Where(match => match is not null && !string.IsNullOrWhiteSpace(match.Requirement))
            .GroupBy(match => NormalizeRequirement(match.Requirement), StringComparer.OrdinalIgnoreCase)
            .Where(group => !gapKeys.Contains(group.Key))
            .Select(group => group.First())
            .ToList();

        var breakdown = new FitScoreBreakdown();
        double earned = 0, possible = 0;

        foreach (var match in distinctMatches)
        {
            match.Importance = NormalizeImportance(match.Importance, match.Requirement);
            var (strength, credit) = EvidenceStrength(match.EvidenceIds);
            match.EvidenceStrength = strength;

            var weight = Weight(match.Importance);
            earned += weight * credit;
            possible += weight;

            if (IsImportance(match.Importance, "Must-have"))
            {
                breakdown.MustHaveMet++;
                breakdown.MustHaveTotal++;
            }
            else if (IsImportance(match.Importance, "Preferred"))
            {
                breakdown.PreferredMet++;
                breakdown.PreferredTotal++;
            }

            switch (strength)
            {
                case "Professional": breakdown.ProfessionalEvidence++; break;
                case "Internship": breakdown.InternshipEvidence++; break;
                case "Project": breakdown.ProjectEvidence++; break;
            }
        }

        foreach (var gap in distinctGaps)
        {
            var importance = NormalizeImportance(gap.Severity, gap.Requirement);
            var weight = Weight(importance);
            possible += weight;
            earned += IsStatus(gap, "Unmet") ? 0 : weight * UnverifiedGapCredit;

            if (IsImportance(importance, "Must-have")) breakdown.MustHaveTotal++;
            else if (IsImportance(importance, "Preferred")) breakdown.PreferredTotal++;
        }

        var requirementCount = distinctMatches.Count + distinctGaps.Count;
        var coverage = possible > 0 ? earned / possible : 0.5;
        var score = (int)Math.Round(coverage * 100, MidpointRounding.AwayFromZero);

        // With very few explicit requirements the evidence is thin; pull the score towards the middle.
        if (requirementCount < 3)
        {
            breakdown.Confidence = "Low";
            score = (int)Math.Round((score + 50) / 2.0, MidpointRounding.AwayFromZero);
        }

        breakdown.CoverageScore = Math.Clamp(score, 0, 100);

        var unmetMustHave = distinctGaps.Any(gap => IsSeverity(gap, "Must-have") && IsStatus(gap, "Unmet"));
        var unverifiedMustHave = distinctGaps.Any(gap => IsSeverity(gap, "Must-have") && !IsStatus(gap, "Unmet"));
        if (unmetMustHave && score > UnmetMustHaveCap)
        {
            score = UnmetMustHaveCap;
            breakdown.CapReason = "A mandatory requirement is not met.";
        }
        else if (unverifiedMustHave && score > UnverifiedMustHaveCap)
        {
            score = UnverifiedMustHaveCap;
            breakdown.CapReason = "A mandatory requirement still needs verification.";
        }

        result.MatchScore = Math.Clamp(score, 0, 100);
        result.ScoreBreakdown = breakdown;
        result.MandatoryRequirementsStatus = unmetMustHave
            ? "Not met"
            : unverifiedMustHave
                ? "Needs verification"
                : "No unresolved mandatory gaps";

        var evidence = new List<string>();
        if (breakdown.ProfessionalEvidence > 0) evidence.Add($"{breakdown.ProfessionalEvidence} from professional work");
        if (breakdown.InternshipEvidence > 0) evidence.Add($"{breakdown.InternshipEvidence} from an internship");
        if (breakdown.ProjectEvidence > 0) evidence.Add($"{breakdown.ProjectEvidence} from projects or study");

        var explanation =
            $"Fit score {result.MatchScore}/100 from the evidence: must-haves {breakdown.MustHaveMet}/{breakdown.MustHaveTotal} met, " +
            $"preferred {breakdown.PreferredMet}/{breakdown.PreferredTotal}" +
            (evidence.Count > 0 ? $"; matches backed {string.Join(", ", evidence)}" : string.Empty) +
            (breakdown.CapReason is null ? string.Empty : $"; capped at {result.MatchScore} because {breakdown.CapReason.TrimEnd('.').ToLowerInvariant()}") +
            (breakdown.Confidence == "Low" ? "; few explicit requirements, so the score is less certain" : string.Empty) +
            ". This is a heuristic fit score, not a probability of receiving an offer.";
        result.Rationale = AppendOnce(result.Rationale, explanation);

        ApplyRecommendationPolicy(result, distinctGaps);

        // Normalize occasional model-generated missing spaces in policy phrases.
        result.Rationale = Regex.Replace(
            result.Rationale ?? string.Empty,
            @"\b(Skip|Review|Apply)because\b",
            "$1 because",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        result.Summary = Regex.Replace(
            result.Summary ?? string.Empty,
            @"\b(Skip|Review|Apply)because\b",
            "$1 because",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static void ApplyRecommendationPolicy(
        JobAnalysisResult result,
        IReadOnlyCollection<RequirementGap> gaps)
    {
        var explicitlyUnmetMandatory = gaps.Any(gap =>
            IsSeverity(gap, "Must-have") && IsStatus(gap, "Unmet"));

        if (explicitlyUnmetMandatory)
        {
            // Remove model-generated wording that treats an explicitly unmet requirement
            // as merely unknown or something that can be cleared up with a question.
            result.Summary = RemoveUnmetContradictions(result.Summary);
            result.Rationale = RemoveUnmetContradictions(result.Rationale);

            result.Recommendation = "Skip";
            result.Rationale = AppendOnce(
                result.Rationale,
                "Recommendation set to Skip because the candidate profile explicitly conflicts with a mandatory requirement in the vacancy.");

            return;
        }

        if (result.MatchScore < SkipThreshold)
        {
            result.Recommendation = "Skip";
            result.Rationale = AppendOnce(
                result.Rationale,
                $"Recommendation set to Skip because the profile covers too little of what the vacancy asks for (fit score below {SkipThreshold}).");
            return;
        }

        var hasMandatoryGap = gaps.Any(gap => IsSeverity(gap, "Must-have"));
        if (hasMandatoryGap)
        {
            if (!string.Equals(result.Recommendation, "Review", StringComparison.OrdinalIgnoreCase))
            {
                result.Recommendation = "Review";
                result.Rationale = AppendOnce(
                    result.Rationale,
                    "Recommendation set to Review because at least one mandatory requirement remains unverified.");
            }

            return;
        }

        // Validation warnings no longer force Review on their own: unproven matches are already
        // scored as unverified gaps, and untraceable requirements are excluded from the score.
        // Once hard gates and evidence validation are resolved, the final recommendation
        // follows the explicit fit threshold instead of allowing the model's label to override it.
        var hasOnlyNonBlockingGaps = gaps.All(gap => IsSeverity(gap, "Preferred"));
        if (result.MatchScore >= ApplyThreshold && hasOnlyNonBlockingGaps)
        {
            if (!string.Equals(result.Recommendation, "Apply", StringComparison.OrdinalIgnoreCase))
            {
                result.Rationale = AppendOnce(
                    result.Rationale,
                    $"Recommendation set to Apply because no mandatory gaps remain, all remaining gaps are preferred, and the fit score is at least {ApplyThreshold}.");
            }

            result.Recommendation = "Apply";
            return;
        }

        if (!string.Equals(result.Recommendation, "Review", StringComparison.OrdinalIgnoreCase))
        {
            result.Rationale = AppendOnce(
                result.Rationale,
                "Recommendation set to Review because no mandatory requirement is explicitly unmet, but the calibrated fit score or remaining gaps do not meet the Apply threshold.");
        }

        result.Recommendation = "Review";
    }

    private static string RemoveUnmetContradictions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
        var retained = sentences.Where(sentence =>
            !Regex.IsMatch(
                sentence,
                @"\b(can|could|may)\s+be\s+verified\b|\bneed(s)?\s+to\s+be\s+verified\b|\bneeds?\s+verification\b|\bcan\s+be\s+cleared\s+up\b|\bcreates?\s+(?:a\s+)?(?:must-have\s+)?gap\s+requiring\s+verification\b|\b(?:must-have\s+)?gap\s+(?:that\s+)?(?:requires?|needs?)\s+verification\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

        return string.Join(" ", retained).Trim();
    }

    private static int GapImportanceOrder(RequirementGap gap) =>
        IsSeverity(gap, "Must-have") ? 0 : IsSeverity(gap, "Preferred") ? 2 : 1;

    private static string NormalizeImportance(string? importance, string requirement)
    {
        var value = importance?.Trim() ?? string.Empty;
        if (value.Equals("Must-have", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Required", StringComparison.OrdinalIgnoreCase))
        {
            return "Must-have";
        }

        if (value.Equals("Preferred", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Nice-to-have", StringComparison.OrdinalIgnoreCase) ||
            requirement.Contains("(preferred)", StringComparison.OrdinalIgnoreCase))
        {
            return "Preferred";
        }

        return "Unknown";
    }

    private static double Weight(string importance) =>
        IsImportance(importance, "Must-have") ? MustHaveWeight
        : IsImportance(importance, "Preferred") ? PreferredWeight
        : UnknownWeight;

    private static bool IsImportance(string importance, string value) =>
        string.Equals(importance, value, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Credits a match by the average strength of its cited profile facts (so "SQL and Python" backed by
    /// a professional SQL fact and a project-only Python fact earns partial credit). The fact-ID prefix shows
    /// where the evidence comes from, so a personal project cannot count as much as the same skill used in a job.
    /// The label is the strongest source cited.
    /// </summary>
    public static (string Strength, double Credit) EvidenceStrength(IEnumerable<string>? evidenceIds)
    {
        var cited = (evidenceIds ?? [])
            .Select(id => (id ?? string.Empty).Split('-')[0].ToUpperInvariant() switch
            {
                "EXP" or "DATE" => ("Professional", 1.0),
                "EDU" or "LAN" or "CER" or "AUTH" => ("Credential", 1.0),
                "SKL" => ("Professional", 0.85),
                "INT" => ("Internship", 0.8),
                "SUM" or "NOTE" => ("Professional", 0.7),
                "PRJ" or "PJT" => ("Project", 0.6),
                _ => ("None", 0.4)
            })
            .ToList();

        if (cited.Count == 0)
        {
            return ("None", 0.4);
        }

        var strongest = cited.MaxBy(item => item.Item2);
        return (strongest.Item1, cited.Average(item => item.Item2));
    }

    private static bool IsSeverity(RequirementGap gap, string value) =>
        string.Equals(gap.Severity, value, StringComparison.OrdinalIgnoreCase);

    private static bool IsStatus(RequirementGap gap, string value) =>
        string.Equals(gap.Status, value, StringComparison.OrdinalIgnoreCase);

    private static string AppendOnce(string? existing, string addition)
    {
        if (!string.IsNullOrWhiteSpace(existing) &&
            existing.Contains(addition, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return string.IsNullOrWhiteSpace(existing)
            ? addition
            : $"{existing.TrimEnd()} {addition}";
    }

    private static string NormalizeRequirement(string requirement) =>
        string.Join(" ", requirement.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToLowerInvariant();
}
