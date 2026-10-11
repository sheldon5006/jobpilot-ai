using JobPilot.Api.Models;
using JobPilot.Api.Services;
using Xunit;

namespace JobPilot.Api.Tests;

public sealed class EvidenceAndEligibilityTests
{
    [Fact]
    public void IrrelevantAuthorizationQuestionIsRemoved()
    {
        var request = new JobAnalysisRequest
        {
            JobTitle = ".NET Developer",
            JobDescription = "Build web applications using C#, ASP.NET Core, SQL Server, and REST APIs."
        };
        var profile = new CandidateProfile
        {
            ProfessionalSummary = "Software developer with web application experience.",
            ProfessionalSkills = ["C#", "ASP.NET Core"]
        };
        var result = new JobAnalysisResult
        {
            QuestionsToVerify =
            [
                "Do you have valid, unrestricted authorization to work in the country where this position is based?",
                "Are you legally authorized to work in Germany?",
                "Do you require visa sponsorship?"
            ],
            Gaps = [new RequirementGap
            {
                Requirement = "work authorization",
                Severity = "Must-have",
                Status = "Unverified",
                Explanation = "Model-generated irrelevant gap."
            }]
        };

        JobRequirementRuleEngine.Apply(request, profile, result);

        Assert.DoesNotContain(result.QuestionsToVerify,
            question => question.Contains("authorization to work", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.QuestionsToVerify,
            question => question.Contains("authorized to work", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.QuestionsToVerify,
            question => question.Contains("visa sponsorship", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Gaps,
            gap => gap.Requirement.Contains("work authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExplicitAuthorizationConditionCreatesUnverifiedGateWhenProfileIsBlank()
    {
        var request = new JobAnalysisRequest
        {
            JobTitle = ".NET Developer",
            JobDescription = "Applicants must already have the legal right to work in Germany. The employer cannot provide visa sponsorship. Build APIs using C# and SQL Server."
        };
        var result = new JobAnalysisResult();
        JobRequirementRuleEngine.Apply(request, new CandidateProfile
        {
            ProfessionalSummary = "Software developer.",
            ProfessionalSkills = ["C#", "SQL Server"]
        }, result);

        Assert.Contains(result.Gaps, gap =>
            gap.Severity == "Must-have" &&
            gap.Status == "Unverified" &&
            gap.Requirement.Contains("work", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.QuestionsToVerify,
            question => question.Contains("author", StringComparison.OrdinalIgnoreCase) ||
                        question.Contains("work", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnsupportedRequirementsAreRemovedAndWarned()
    {
        var request = new JobAnalysisRequest
        {
            JobTitle = ".NET Developer",
            JobDescription = "Build web applications using C#, ASP.NET Core, SQL Server, and Angular."
        };
        var result = new JobAnalysisResult
        {
            MatchedRequirements =
            [
                new MatchedRequirement
                {
                    Requirement = "Kubernetes and Angular",
                    Evidence = "Angular",
                    EvidenceIds = ["SKL-001"]
                }
            ],
            Gaps =
            [
                new RequirementGap
                {
                    Requirement = "German C2",
                    Severity = "Must-have",
                    Status = "Unmet",
                    Explanation = "This requirement was never stated in the vacancy."
                }
            ]
        };

        VacancyRequirementValidator.Apply(request, result);

        Assert.Empty(result.MatchedRequirements);
        Assert.Empty(result.Gaps);
        Assert.Equal(2, result.RequirementValidationWarnings.Count);
        Assert.Equal("Review", result.Recommendation);
    }

    [Fact]
    public void GermanProficiencyGapPreservesVacancyCeFRLevel()
    {
        var request = new JobAnalysisRequest
        {
            JobTitle = ".NET Developer",
            JobDescription = "German language skills at CEFR C2 are mandatory for this position."
        };
        var result = new JobAnalysisResult
        {
            Gaps =
            [
                new RequirementGap
                {
                    Requirement = "German proficiency (C2)",
                    Severity = "Must-have",
                    Status = "Unmet",
                    Explanation = "The profile does not establish C2."
                }
            ]
        };

        VacancyRequirementValidator.Apply(request, result);

        Assert.Single(result.Gaps);
        Assert.Empty(result.RequirementValidationWarnings);
    }

    [Fact]
    public void GermanProficiencyParaphraseMatchesVacancyLanguageSkillsWording()
    {
        var request = new JobAnalysisRequest
        {
            JobTitle = "Full-Stack Developer",
            JobDescription = "German language skills are preferred but not mandatory."
        };
        var result = new JobAnalysisResult
        {
            Gaps =
            [
                new RequirementGap
                {
                    Requirement = "German proficiency",
                    Severity = "Preferred",
                    Status = "Unmet",
                    Explanation = "German is preferred."
                }
            ]
        };

        VacancyRequirementValidator.Apply(request, result);

        Assert.Single(result.Gaps);
        Assert.Empty(result.RequirementValidationWarnings);
    }

    [Fact]
    public void AuthorizationRequirementMatchesEquivalentRightToWorkWording()
    {
        var request = new JobAnalysisRequest
        {
            JobTitle = ".NET Developer",
            JobDescription = "Applicants must already have the legal right to work in Germany. The employer cannot provide visa sponsorship."
        };
        var result = new JobAnalysisResult
        {
            Gaps =
            [
                new RequirementGap
                {
                    Requirement = "work authorization",
                    Severity = "Must-have",
                    Status = "Unverified",
                    Explanation = "The vacancy explicitly requires existing work eligibility."
                }
            ]
        };

        VacancyRequirementValidator.Apply(request, result);

        Assert.Single(result.Gaps);
        Assert.Empty(result.RequirementValidationWarnings);
    }

    [Fact]
    public void ProfessionalExperienceCannotBeProvenBySkillListAlone()
    {
        var profile = new CandidateProfile
        {
            ProfessionalSkills = ["Angular"]
        };
        var result = new JobAnalysisResult
        {
            MatchedRequirements =
            [
                new MatchedRequirement
                {
                    Requirement = "Practical development experience with Angular",
                    Evidence = "Angular",
                    EvidenceIds = ["SKL-001"]
                }
            ]
        };

        ProfileEvidenceValidator.Apply(profile, result);

        Assert.Empty(result.MatchedRequirements);
        Assert.NotEmpty(result.EvidenceValidationWarnings);
        Assert.Equal("Review", result.Recommendation);
    }

    [Fact]
    public void RealEducationClauseSurvivesFollowingPlaceholderInstructions()
    {
        var profile = new CandidateProfile
        {
            Education = ["M.Sc. Artificial Intelligence — in progress; replace institution and expected completion details as needed."]
        };

        var facts = ProfileEvidenceCatalog.Create(profile);
        var educationFact = Assert.Single(facts.Where(fact => fact.Category == "education"));

        Assert.Equal("M.Sc. Artificial Intelligence — in progress", educationFact.Text);
    }

    [Fact]
    public void LearningLanguageLevelSurvivesTemplateGuidance()
    {
        var profile = new CandidateProfile
        {
            Languages = [new LanguageEntry
            {
                Language = "German",
                Proficiency = "Learning; add a CEFR level only if verified"
            }]
        };

        var languageFact = Assert.Single(ProfileEvidenceCatalog.Create(profile)
            .Where(fact => fact.Category == "language"));

        Assert.Equal("LAN-001", languageFact.Id);
        Assert.Equal("German: Learning", languageFact.Text);
    }

    [Fact]
    public void ReplacedLegacyApplicationsAreNotMistakenForPlaceholders()
    {
        var profile = new CandidateProfile
        {
            Experience =
            [
                new ExperienceEntry
                {
                    Role = "Software Engineer",
                    Evidence = ["Replaced legacy VB applications with ASP.NET Core and Angular."]
                }
            ]
        };

        var facts = ProfileEvidenceCatalog.Create(profile);
        Assert.Contains(facts, fact => fact.Text.StartsWith("Replaced legacy", StringComparison.Ordinal));
    }

    [Fact]
    public void EvidenceTextIsRenderedFromTheCitedProfileFact()
    {
        const string fact = "Designed and integrated REST APIs with Entity Framework and SQL Server.";
        var profile = new CandidateProfile
        {
            Experience =
            [
                new ExperienceEntry
                {
                    Role = "Software Engineer",
                    Evidence = [fact]
                }
            ]
        };
        var result = new JobAnalysisResult
        {
            MatchedRequirements =
            [
                new MatchedRequirement
                {
                    Requirement = "Professional experience with REST APIs",
                    Evidence = "Fabricated evidence should be overwritten.",
                    EvidenceIds = ["EXP-001"]
                }
            ]
        };

        ProfileEvidenceValidator.Apply(profile, result);

        var match = Assert.Single(result.MatchedRequirements);
        Assert.Equal(fact, match.Evidence);
        Assert.Equal("EXP-001", Assert.Single(match.EvidenceIds));
        Assert.Empty(result.EvidenceValidationWarnings);
    }

    private static MatchedRequirement Match(string requirement, string importance, params string[] evidenceIds) => new()
    {
        Requirement = requirement,
        Importance = importance,
        Evidence = "Supported by the profile.",
        EvidenceIds = [.. evidenceIds]
    };

    [Fact]
    public void UnmetMandatoryRequirementCapsScoreAndForcesSkip()
    {
        var result = new JobAnalysisResult
        {
            Recommendation = "Apply",
            MatchScore = 90,
            MatchedRequirements = [Match("C#", "Must-have", "EXP-001"), Match("Angular", "Must-have", "EXP-002")],
            Gaps =
            [
                new RequirementGap
                {
                    Requirement = "German C2",
                    Severity = "Must-have",
                    Status = "Unmet",
                    Explanation = "The profile states learning German, not C2."
                }
            ]
        };

        JobFitScoreCalibrator.Apply(result);

        Assert.Equal("Skip", result.Recommendation);
        Assert.Equal("Not met", result.MandatoryRequirementsStatus);
        Assert.True(result.MatchScore <= 35);
        Assert.Equal(1, result.ScoreBreakdown!.MustHaveTotal - result.ScoreBreakdown.MustHaveMet);
    }

    [Fact]
    public void FullyEvidencedProfessionalMatchIsApplyRegardlessOfModelLabel()
    {
        var result = new JobAnalysisResult
        {
            Recommendation = "Skip",
            MatchScore = 10,
            MatchedRequirements =
            [
                Match("C#", "Must-have", "EXP-001"),
                Match("ASP.NET Core", "Must-have", "EXP-002"),
                Match("Angular", "Must-have", "EXP-003"),
                Match("Azure DevOps", "Preferred", "SKL-004")
            ]
        };

        JobFitScoreCalibrator.Apply(result);

        Assert.Equal("Apply", result.Recommendation);
        Assert.True(result.MatchScore >= 95);
        Assert.Equal("No unresolved mandatory gaps", result.MandatoryRequirementsStatus);
        Assert.Equal(3, result.ScoreBreakdown!.MustHaveMet);
        Assert.Equal(1, result.ScoreBreakdown.PreferredMet);
    }

    [Fact]
    public void ProjectOnlyEvidenceScoresLowerThanProfessionalEvidence()
    {
        JobAnalysisResult Build(string prefix) => new()
        {
            MatchedRequirements =
            [
                Match("Python", "Must-have", $"{prefix}-001"),
                Match("Machine learning", "Must-have", $"{prefix}-002"),
                Match("REST APIs", "Must-have", $"{prefix}-003")
            ]
        };

        var professional = Build("EXP");
        var project = Build("PJT");
        JobFitScoreCalibrator.Apply(professional);
        JobFitScoreCalibrator.Apply(project);

        Assert.Equal(100, professional.MatchScore);
        Assert.Equal(60, project.MatchScore);
        Assert.Equal("Review", project.Recommendation);
        Assert.Equal(3, project.ScoreBreakdown!.ProjectEvidence);
    }

    [Fact]
    public void UnverifiedMandatoryRequirementCapsBelowApply()
    {
        var result = new JobAnalysisResult
        {
            Recommendation = "Apply",
            MatchedRequirements =
            [
                Match("C#", "Must-have", "EXP-001"),
                Match("SQL Server", "Must-have", "EXP-002"),
                Match("Angular", "Must-have", "EXP-003")
            ],
            Gaps =
            [
                new RequirementGap
                {
                    Requirement = "Current student enrolment",
                    Severity = "Must-have",
                    Status = "Unverified",
                    Explanation = "Current enrolment is not established."
                }
            ]
        };

        JobFitScoreCalibrator.Apply(result);

        Assert.Equal("Review", result.Recommendation);
        Assert.Equal("Needs verification", result.MandatoryRequirementsStatus);
        Assert.True(result.MatchScore < JobFitScoreCalibrator.ApplyThreshold);
        Assert.NotNull(result.ScoreBreakdown!.CapReason);
    }

    [Fact]
    public void LowCoverageIsSkip()
    {
        var result = new JobAnalysisResult
        {
            MatchedRequirements = [Match("Teamwork", "Preferred", "SUM-001")],
            Gaps =
            [
                new RequirementGap { Requirement = "Building physics", Severity = "Must-have", Status = "Unverified" },
                new RequirementGap { Requirement = "Energy simulation tools", Severity = "Must-have", Status = "Unverified" },
                new RequirementGap { Requirement = "HVAC knowledge", Severity = "Must-have", Status = "Unverified" }
            ]
        };

        JobFitScoreCalibrator.Apply(result);

        Assert.True(result.MatchScore < JobFitScoreCalibrator.SkipThreshold);
        Assert.Equal("Skip", result.Recommendation);
    }

    [Fact]
    public void RequirementListedAsMatchAndGapCountsOnceAsGap()
    {
        var result = new JobAnalysisResult
        {
            MatchedRequirements = [Match("Kubernetes", "Must-have", "PRJ-001"), Match("C#", "Must-have", "EXP-001"), Match("SQL", "Must-have", "EXP-002")],
            Gaps = [new RequirementGap { Requirement = "kubernetes", Severity = "Must-have", Status = "Unverified" }]
        };

        JobFitScoreCalibrator.Apply(result);

        Assert.Equal(2, result.ScoreBreakdown!.MustHaveMet);
        Assert.Equal(3, result.ScoreBreakdown.MustHaveTotal);
    }

    [Fact]
    public void FewRequirementsLowerConfidenceAndPullTowardsMiddle()
    {
        var result = new JobAnalysisResult { MatchedRequirements = [Match("C#", "Must-have", "EXP-001")] };

        JobFitScoreCalibrator.Apply(result);

        Assert.Equal("Low", result.ScoreBreakdown!.Confidence);
        Assert.Equal(75, result.MatchScore);
    }
}
