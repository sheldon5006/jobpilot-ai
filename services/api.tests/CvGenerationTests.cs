using System.IO.Compression;
using System.Xml.Linq;
using JobPilot.Api.Models;
using JobPilot.Api.Services;
using Xunit;

namespace JobPilot.Api.Tests;

public sealed class CvGenerationTests
{
    private static CandidateProfile CreateProfile() => new()
    {
        ProfessionalSummary = "Software engineer focused on .NET web applications.",
        TargetRoles = [".NET Developer"],
        ProfessionalSkills = ["C#", "ASP.NET Core", "SQL Server"],
        ProjectAndAcademicSkills = ["Python"],
        Experience =
        [
            new ExperienceEntry { Role = "Software Engineer", Period = "2021 – 2024", Evidence = ["Built REST APIs."] },
            new ExperienceEntry { Role = "Software Developer Intern", Period = "2020", Evidence = ["Developed a student portal."] }
        ],
        Education = ["MSc Artificial Intelligence — in progress"],
        Languages = [new LanguageEntry { Language = "English", Proficiency = "C1" }],
        Certifications = [],
        Contact = new ContactDetails { FullName = "Alex Example", Email = "alex@example.com" }
    };

    [Fact]
    public void SanitizerDropsSkillsThatAreNotInTheProfile()
    {
        var raw = new CvDocument
        {
            SkillGroups =
            [
                new CvSkillGroup { Category = "Backend", Skills = ["c#", "Kubernetes", "ASP.NET Core"] },
                new CvSkillGroup { Category = "Invented", Skills = ["Rust"] }
            ]
        };

        var cv = CvSanitizer.Apply(raw, CreateProfile());

        var group = Assert.Single(cv.SkillGroups);
        Assert.Equal(["C#", "ASP.NET Core"], group.Skills);
    }

    [Fact]
    public void SanitizerTakesRolesAndDatesFromTheProfileAndKeepsEveryRole()
    {
        var raw = new CvDocument
        {
            Experience =
            [
                new CvExperience { SourceIndex = 0, Role = "Principal Architect", Period = "2010 – 2024", Bullets = ["Designed REST APIs with ASP.NET Core."] },
                new CvExperience { SourceIndex = 7, Role = "Invented role", Bullets = ["Did things."] }
            ]
        };

        var cv = CvSanitizer.Apply(raw, CreateProfile());

        Assert.Equal(2, cv.Experience.Count);
        Assert.Equal("Software Engineer", cv.Experience[0].Role);
        Assert.Equal("2021 – 2024", cv.Experience[0].Period);
        Assert.Equal(["Designed REST APIs with ASP.NET Core."], cv.Experience[0].Bullets);
        Assert.Equal("Software Developer Intern", cv.Experience[1].Role);
        Assert.Equal(["Developed a student portal."], cv.Experience[1].Bullets);
    }

    [Fact]
    public void SanitizerCopiesEducationAndLanguagesFromTheProfile()
    {
        var raw = new CvDocument
        {
            Education = ["PhD from an invented university"],
            Languages = ["German — C2"]
        };

        var cv = CvSanitizer.Apply(raw, CreateProfile());

        Assert.Equal(["MSc Artificial Intelligence — in progress"], cv.Education);
        Assert.Equal(["English — C1"], cv.Languages);
    }

    [Fact]
    public void SanitizerTakesProjectFactsFromTheProfileAndIgnoresUnknownProjects()
    {
        var profile = new CandidateProfile
        {
            ProfessionalSummary = "Engineer.",
            ProfessionalSkills = ["C#"],
            Projects =
            [
                new ProjectEntry
                {
                    Name = "FlowDesk",
                    Context = "Personal project",
                    Url = "github.com/example/FlowDesk",
                    Highlights = ["Built a REST API."],
                    Technologies = [".NET 10", "PostgreSQL"]
                }
            ]
        };
        var raw = new CvDocument
        {
            Projects =
            [
                new CvProject { SourceIndex = 0, Name = "Renamed", Technologies = ["Kubernetes"], Bullets = ["Built a scheduling REST API."] },
                new CvProject { SourceIndex = 3, Name = "Invented project", Bullets = ["Did things."] }
            ]
        };

        var cv = CvSanitizer.Apply(raw, profile);

        var project = Assert.Single(cv.Projects);
        Assert.Equal("FlowDesk", project.Name);
        Assert.Equal("github.com/example/FlowDesk", project.Url);
        Assert.Equal([".NET 10", "PostgreSQL"], project.Technologies);
        Assert.Equal(["Built a scheduling REST API."], project.Bullets);
    }

    [Fact]
    public void ProjectFactsAreNotProfessionalExperience()
    {
        var profile = new CandidateProfile
        {
            Projects = [new ProjectEntry { Name = "FlowDesk", Description = "Scheduling platform.", Highlights = ["Built a REST API."] }]
        };

        var facts = ProfileEvidenceCatalog.Create(profile);

        Assert.NotEmpty(facts);
        Assert.All(facts, fact => Assert.Equal("personal_project", fact.Category));
    }

    [Fact]
    public void AiProfileCopyExcludesContactDetails()
    {
        var copy = CreateProfile().WithoutContact();

        Assert.Equal(string.Empty, copy.Contact.FullName);
        Assert.Equal(string.Empty, copy.Contact.Email);
        Assert.Equal("Software engineer focused on .NET web applications.", copy.ProfessionalSummary);
    }

    [Fact]
    public void PdfBuilderWritesAPdfDocument()
    {
        var profile = CreateProfile();
        var cv = CvSanitizer.Apply(new CvDocument { Headline = "C# Developer", Summary = "Builds APIs → ships features." }, profile);

        var bytes = CvPdfBuilder.Build(profile.Contact, cv);

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public void DocxBuilderWritesAValidWordDocument()
    {
        var profile = CreateProfile();
        var cv = CvSanitizer.Apply(new CvDocument { Headline = "C# & <.NET> Developer", Summary = "Builds APIs." }, profile);

        var bytes = CvDocxBuilder.Build(profile.Contact, cv);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("_rels/.rels"));
        var documentEntry = archive.GetEntry("word/document.xml");
        Assert.NotNull(documentEntry);

        using var stream = documentEntry!.Open();
        var document = XDocument.Load(stream);
        var text = string.Concat(document.Descendants().Where(node => node.Name.LocalName == "t").Select(node => node.Value));
        Assert.Contains("Alex Example", text);
        Assert.Contains("C# & <.NET> Developer", text);
        Assert.Contains("Software Engineer", text);
    }
}
