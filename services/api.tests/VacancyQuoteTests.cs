using JobPilot.Api.Models;
using JobPilot.Api.Services;
using Xunit;

namespace JobPilot.Api.Tests;

public sealed class VacancyQuoteTests
{
    private static readonly JobAnalysisRequest GermanVacancy = new()
    {
        JobTitle = "Software Engineer - Angular/.NET (m/w/d)",
        JobDescription = """
            Ihr Profil: Abgeschlossenes Studium der Informatik oder eine vergleichbare Ausbildung.
            Sehr gute Kenntnisse in Angular und TypeScript sowie in .NET und C#.
            Erfahrung mit SQL-Datenbanken ist von Vorteil. Teamfähigkeit und Größe im Denken.
            """
    };

    [Fact]
    public void EnglishRequirementWithVerbatimGermanQuoteIsKept()
    {
        var result = new JobAnalysisResult
        {
            MatchedRequirements =
            [
                new MatchedRequirement
                {
                    Requirement = "Proficiency in Angular and TypeScript",
                    Importance = "Must-have",
                    VacancyQuote = "Sehr gute Kenntnisse in Angular und TypeScript",
                    EvidenceIds = ["EXP-001"]
                }
            ],
            Gaps =
            [
                new RequirementGap
                {
                    Requirement = "Completed computer science degree",
                    Severity = "Must-have",
                    Status = "Unverified",
                    VacancyQuote = "Abgeschlossenes Studium der Informatik"
                }
            ]
        };

        VacancyRequirementValidator.Apply(GermanVacancy, result);

        Assert.Single(result.MatchedRequirements);
        Assert.Single(result.Gaps);
        Assert.Empty(result.RequirementValidationWarnings);
    }

    [Fact]
    public void InventedRequirementWithFabricatedQuoteIsRemoved()
    {
        var result = new JobAnalysisResult
        {
            Gaps =
            [
                new RequirementGap
                {
                    Requirement = "Kubernetes certification",
                    Severity = "Must-have",
                    Status = "Unverified",
                    VacancyQuote = "Zertifizierung in Kubernetes und AWS erforderlich"
                }
            ]
        };

        VacancyRequirementValidator.Apply(GermanVacancy, result);

        Assert.Empty(result.Gaps);
        Assert.Single(result.RequirementValidationWarnings);
    }

    private static readonly CandidateProfile Profile = new()
    {
        ProfessionalSummary = "Software engineer.",
        ProfessionalSkills = ["C#", "Python"],
        Experience = [new ExperienceEntry { Role = "Software Engineer", Period = "2021 – 2024", Evidence = ["Built REST APIs in C#."] }],
        Languages = [new LanguageEntry { Language = "English", Proficiency = "Fluent" }]
    };

    [Theory]
    [InlineData("Proficiency in C#", "SKL-001")]
    [InlineData("Scripting language (Python)", "SKL-002")]
    public void ProgrammingLanguagesAreNotTreatedAsSpokenLanguages(string requirement, string factId)
    {
        var result = new JobAnalysisResult
        {
            MatchedRequirements = [new MatchedRequirement { Requirement = requirement, Importance = "Must-have", EvidenceIds = [factId] }]
        };

        ProfileEvidenceValidator.Apply(Profile, result);

        Assert.Single(result.MatchedRequirements);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void MatchWithoutValidEvidenceBecomesUnverifiedGapInsteadOfDisappearing()
    {
        var result = new JobAnalysisResult
        {
            MatchedRequirements =
            [
                new MatchedRequirement { Requirement = "Kubernetes", Importance = "Must-have", VacancyQuote = "Kubernetes", EvidenceIds = ["XYZ-999"] }
            ]
        };

        ProfileEvidenceValidator.Apply(Profile, result);

        Assert.Empty(result.MatchedRequirements);
        var gap = Assert.Single(result.Gaps);
        Assert.Equal("Must-have", gap.Severity);
        Assert.Equal("Unverified", gap.Status);
        Assert.Single(result.EvidenceValidationWarnings);
    }

    [Fact]
    public void UnmetWithoutContradictingFactBecomesUnverified()
    {
        var profile = new CandidateProfile { ProfessionalSummary = "Engineer.", Constraints = ["Based in Berlin, Germany."] };
        var result = new JobAnalysisResult
        {
            Gaps =
            [
                new RequirementGap { Requirement = "Node.js experience", Severity = "Must-have", Status = "Unmet" },
                new RequirementGap { Requirement = "On-site in Munich", Severity = "Must-have", Status = "Unmet", ConflictEvidenceIds = ["NOTE-001"] },
                new RequirementGap { Requirement = "Go experience", Severity = "Must-have", Status = "Unmet", ConflictEvidenceIds = ["FAKE-001"] }
            ]
        };

        ProfileEvidenceValidator.RequireConflictEvidence(profile, result);

        Assert.Equal("Unverified", result.Gaps[0].Status);
        Assert.Equal("Unmet", result.Gaps[1].Status);
        Assert.Equal("Unverified", result.Gaps[2].Status);
    }

    [Fact]
    public void CombinedEvidenceIsAveragedNotMaxed()
    {
        var (strength, credit) = JobFitScoreCalibrator.EvidenceStrength(["SKL-001", "PRJ-002"]);

        Assert.Equal("Professional", strength);
        Assert.Equal(0.725, credit, 3);
    }

    [Fact]
    public void RequirementWithoutQuoteStillUsesWordCheck()
    {
        var result = new JobAnalysisResult
        {
            MatchedRequirements =
            [
                new MatchedRequirement { Requirement = "Angular", Importance = "Must-have" },
                new MatchedRequirement { Requirement = "React", Importance = "Must-have" }
            ]
        };

        VacancyRequirementValidator.Apply(GermanVacancy, result);

        var kept = Assert.Single(result.MatchedRequirements);
        Assert.Equal("Angular", kept.Requirement);
    }
}
