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

    [Fact]
    public void MandatoryFailureIsSeparateFromFitScore()
    {
        var result = new JobAnalysisResult
        {
            Recommendation = "Apply",
            MatchScore = 90,
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
        Assert.Equal(50, result.MatchScore);
    }

    [Fact]
    public void UnverifiedMandatoryRequirementProducesReviewStatus()
    {
        var result = new JobAnalysisResult
        {
            Recommendation = "Apply",
            MatchScore = 85,
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
    }
}
