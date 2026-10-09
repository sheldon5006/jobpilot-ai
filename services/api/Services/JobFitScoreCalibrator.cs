using JobPilot.Api.Models;

namespace JobPilot.Api.Services;

/// <summary>
/// Applies a transparent, deterministic deduction to the model's initial fit estimate.
/// This is a heuristic, not a statistically calibrated probability of getting hired.
/// </summary>
public static class JobFitScoreCalibrator
{
    private const int MustHavePenaltyPerGap = 20;
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
                .OrderByDescending(gap => GetPenalty(gap.Severity))
                .First())
            .ToList();

        var mustHaveCount = distinctGaps.Count(gap =>
            string.Equals(gap.Severity, "Must-have", StringComparison.OrdinalIgnoreCase));

        var preferredCount = distinctGaps.Count(gap =>
            string.Equals(gap.Severity, "Preferred", StringComparison.OrdinalIgnoreCase));

        var unknownSeverityCount = distinctGaps.Count(gap =>
            !string.Equals(gap.Severity, "Must-have", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(gap.Severity, "Preferred", StringComparison.OrdinalIgnoreCase));

        var mustHavePenalty = Math.Min(mustHaveCount * MustHavePenaltyPerGap, MaxMustHavePenalty);
        var preferredPenalty = Math.Min(preferredCount * PreferredPenaltyPerGap, MaxPreferredPenalty);
        var unknownSeverityPenalty = Math.Min(
            unknownSeverityCount * UnknownSeverityPenaltyPerGap,
            MaxUnknownSeverityPenalty);

        var totalPenalty = Math.Min(
            mustHavePenalty + preferredPenalty + unknownSeverityPenalty,
            MaxTotalPenalty);

        result.MatchScore = Math.Clamp(modelScore - totalPenalty, 0, 100);

        var adjustments = new List<string>();
        if (mustHavePenalty > 0)
        {
            adjustments.Add($"{mustHaveCount} must-have gap(s): -{mustHavePenalty}");
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

        if (totalPenalty < mustHavePenalty + preferredPenalty + unknownSeverityPenalty)
        {
            adjustmentText += $"; total deduction capped at {MaxTotalPenalty} points";
        }

        var explanation =
            $"Score calibration: model estimate {modelScore}/100; {adjustmentText}; calibrated score {result.MatchScore}/100. This is a heuristic fit score, not a probability of receiving an offer.";

        if (!result.Rationale.Contains(explanation, StringComparison.OrdinalIgnoreCase))
        {
            result.Rationale = string.IsNullOrWhiteSpace(result.Rationale)
                ? explanation
                : $"{result.Rationale.TrimEnd()} {explanation}";
        }
    }

    private static int GetPenalty(string? severity)
    {
        if (string.Equals(severity, "Must-have", StringComparison.OrdinalIgnoreCase))
        {
            return MustHavePenaltyPerGap;
        }

        if (string.Equals(severity, "Preferred", StringComparison.OrdinalIgnoreCase))
        {
            return PreferredPenaltyPerGap;
        }

        return UnknownSeverityPenaltyPerGap;
    }

    private static string NormalizeRequirement(string requirement) =>
        string.Join(" ", requirement.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToLowerInvariant();
}
