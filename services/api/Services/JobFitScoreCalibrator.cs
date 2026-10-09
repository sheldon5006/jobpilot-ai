using System.Text.RegularExpressions;
using JobPilot.Api.Models;

namespace JobPilot.Api.Services;

/// <summary>
/// Applies transparent, deterministic deductions to the model's initial fit estimate.
/// This is a heuristic, not a statistically calibrated probability of getting hired.
/// </summary>
public static class JobFitScoreCalibrator
{
    private const int UnmetMustHavePenaltyPerGap = 40;
    private const int UnverifiedMustHavePenaltyPerGap = 20;
    private const int PreferredPenaltyPerGap = 5;
    private const int UnknownSeverityPenaltyPerGap = 8;

    private const int MaxMustHavePenalty = 50;
    private const int MaxPreferredPenalty = 20;
    private const int MaxUnknownSeverityPenalty = 24;
    private const int MaxTotalPenalty = 60;

    public static void Apply(JobAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var modelScore = Math.Clamp(result.MatchScore, 0, 100);
        var distinctGaps = (result.Gaps ?? [])
            .Where(gap => gap is not null && !string.IsNullOrWhiteSpace(gap.Requirement))
            .GroupBy(gap => NormalizeRequirement(gap.Requirement), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(GetPenalty)
                .First())
            .ToList();

        var mustHaveUnmetCount = distinctGaps.Count(gap =>
            IsSeverity(gap, "Must-have") && IsStatus(gap, "Unmet"));
        var mustHaveUnverifiedCount = distinctGaps.Count(gap =>
            IsSeverity(gap, "Must-have") && !IsStatus(gap, "Unmet"));
        var preferredCount = distinctGaps.Count(gap => IsSeverity(gap, "Preferred"));
        var unknownSeverityCount = distinctGaps.Count(gap =>
            !IsSeverity(gap, "Must-have") && !IsSeverity(gap, "Preferred"));

        var rawMustHavePenalty =
            (mustHaveUnmetCount * UnmetMustHavePenaltyPerGap) +
            (mustHaveUnverifiedCount * UnverifiedMustHavePenaltyPerGap);
        var mustHavePenalty = Math.Min(rawMustHavePenalty, MaxMustHavePenalty);
        var preferredPenalty = Math.Min(preferredCount * PreferredPenaltyPerGap, MaxPreferredPenalty);
        var unknownSeverityPenalty = Math.Min(
            unknownSeverityCount * UnknownSeverityPenaltyPerGap,
            MaxUnknownSeverityPenalty);

        var uncappedTotal = mustHavePenalty + preferredPenalty + unknownSeverityPenalty;
        var totalPenalty = Math.Min(uncappedTotal, MaxTotalPenalty);
        var normalCalibratedScore = Math.Clamp(modelScore - totalPenalty, 0, 100);
        result.MatchScore = normalCalibratedScore;
        result.MandatoryRequirementsStatus = distinctGaps.Any(gap =>
                IsSeverity(gap, "Must-have") && IsStatus(gap, "Unmet"))
            ? "Not met"
            : distinctGaps.Any(gap =>
                IsSeverity(gap, "Must-have") && !IsStatus(gap, "Unmet"))
                ? "Needs verification"
                : "No unresolved mandatory gaps";

        var adjustments = new List<string>();
        if (rawMustHavePenalty > MaxMustHavePenalty)
        {
            adjustments.Add(
                $"{mustHaveUnmetCount} explicitly unmet and {mustHaveUnverifiedCount} unverified must-have gap(s): -{mustHavePenalty} (mandatory-gap deduction capped at {MaxMustHavePenalty})");
        }
        else
        {
            if (mustHaveUnmetCount > 0)
            {
                adjustments.Add($"{mustHaveUnmetCount} explicitly unmet must-have gap(s): -{mustHaveUnmetCount * UnmetMustHavePenaltyPerGap}");
            }

            if (mustHaveUnverifiedCount > 0)
            {
                adjustments.Add($"{mustHaveUnverifiedCount} unverified must-have gap(s): -{mustHaveUnverifiedCount * UnverifiedMustHavePenaltyPerGap}");
            }
        }

        if (preferredPenalty > 0)
        {
            adjustments.Add($"{preferredCount} preferred gap(s): -{preferredPenalty}");
        }

        if (unknownSeverityPenalty > 0)
        {
            adjustments.Add($"{unknownSeverityCount} gap(s) with unknown severity: -{unknownSeverityPenalty}");
        }

        var adjustmentText = adjustments.Count == 0
            ? "no gap-based deductions"
            : string.Join(", ", adjustments);

        if (totalPenalty < uncappedTotal)
        {
            adjustmentText += $"; total deduction capped at {MaxTotalPenalty} points";
        }

        var explanation =
            $"Score calibration: model estimate {modelScore}/100; {adjustmentText}; calibrated score {result.MatchScore}/100. This is a heuristic fit score, not a probability of receiving an offer.";
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

        // Do not let an obsolete Review recommendation caused only by a removed, irrelevant
        // language gap block a strong match. Preferred gaps alone do not require Review.
        if (result.EvidenceValidationWarnings.Count > 0)
        {
            result.Recommendation = "Review";
            result.Rationale = AppendOnce(
                result.Rationale,
                "Recommendation remains Review because one or more matched-requirement evidence references failed validation.");
            return;
        }

        // Preferred gaps alone do not block Apply. Evidence-validation warnings above do.
        var hasOnlyNonBlockingGaps = gaps.All(gap => IsSeverity(gap, "Preferred"));
        if (result.Recommendation == "Review" &&
            result.MatchScore >= 80 &&
            hasOnlyNonBlockingGaps)
        {
            result.Recommendation = "Apply";
            result.Rationale = AppendOnce(
                result.Rationale,
                "Recommendation set to Apply because no mandatory gaps remain; any remaining gaps are preferred rather than required.");
        }
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

    private static int GetPenalty(RequirementGap gap)
    {
        if (IsSeverity(gap, "Must-have"))
        {
            return IsStatus(gap, "Unmet")
                ? UnmetMustHavePenaltyPerGap
                : UnverifiedMustHavePenaltyPerGap;
        }

        if (IsSeverity(gap, "Preferred"))
        {
            return PreferredPenaltyPerGap;
        }

        return UnknownSeverityPenaltyPerGap;
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
