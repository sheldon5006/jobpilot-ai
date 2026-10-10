using JobPilot.Api.Models;

namespace JobPilot.Api.Services;

/// <summary>
/// Treats AI CV output as untrusted. Role titles, periods, education, languages and
/// certifications always come from the profile; skills must exist in the profile.
/// </summary>
public static class CvSanitizer
{
    private const int MaxBullets = 6;
    private const int MaxBulletLength = 400;

    public static CvDocument Apply(CvDocument raw, CandidateProfile profile, string? jobTitle = null)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(profile);

        var profileExperience = profile.Experience ?? [];
        var allowedSkills = (profile.ProfessionalSkills ?? [])
            .Concat(profile.ProjectAndAcademicSkills ?? [])
            .Select(skill => skill.Trim())
            .Where(skill => skill.Length > 0)
            .GroupBy(skill => skill, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var usedSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skillGroups = new List<CvSkillGroup>();
        foreach (var group in raw.SkillGroups ?? [])
        {
            if (group is null)
            {
                continue;
            }

            var skills = new List<string>();
            foreach (var skill in group.Skills ?? [])
            {
                if (skill is not null &&
                    allowedSkills.TryGetValue(skill.Trim(), out var profileSpelling) &&
                    usedSkills.Add(profileSpelling))
                {
                    skills.Add(profileSpelling);
                }
            }

            if (skills.Count > 0)
            {
                skillGroups.Add(new CvSkillGroup
                {
                    Category = Clip(group.Category, 60) is { Length: > 0 } category ? category : "Skills",
                    Skills = skills
                });
            }
        }

        if (skillGroups.Count == 0 && (profile.ProfessionalSkills?.Count ?? 0) > 0)
        {
            skillGroups.Add(new CvSkillGroup { Category = "Technical skills", Skills = [.. profile.ProfessionalSkills!] });
        }

        var experience = new List<CvExperience>();
        var seenIndexes = new HashSet<int>();
        foreach (var entry in raw.Experience ?? [])
        {
            if (entry is null ||
                entry.SourceIndex < 0 ||
                entry.SourceIndex >= profileExperience.Count ||
                !seenIndexes.Add(entry.SourceIndex))
            {
                continue;
            }

            var bullets = CleanBullets(entry.Bullets);
            var source = profileExperience[entry.SourceIndex];
            experience.Add(FromProfile(entry.SourceIndex, source, bullets.Count > 0 ? bullets : CleanBullets(source.Evidence)));
        }

        // A tailored CV must not silently drop roles from the career history.
        for (var index = 0; index < profileExperience.Count; index++)
        {
            if (!seenIndexes.Contains(index))
            {
                experience.Add(FromProfile(index, profileExperience[index], CleanBullets(profileExperience[index].Evidence)));
            }
        }

        experience.Sort((left, right) => left.SourceIndex.CompareTo(right.SourceIndex));

        var headline = Clip(raw.Headline, 140);
        if (headline.Length == 0)
        {
            headline = Clip(profile.TargetRoles?.FirstOrDefault() ?? jobTitle, 140);
        }

        var summary = Clip(raw.Summary, 1500);
        if (summary.Length == 0)
        {
            summary = Clip(profile.ProfessionalSummary, 1500);
        }

        return new CvDocument
        {
            Headline = headline,
            Summary = summary,
            SkillGroups = skillGroups,
            Experience = experience,
            Education = CleanList(profile.Education),
            Languages = (profile.Languages ?? [])
                .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Language))
                .Select(item => string.IsNullOrWhiteSpace(item.Proficiency)
                    ? item.Language.Trim()
                    : $"{item.Language.Trim()} — {item.Proficiency.Trim()}")
                .ToList(),
            Certifications = CleanList(profile.Certifications)
        };
    }

    /// <summary>
    /// Normalises a CV the user edited by hand. User edits are trusted, so content is kept,
    /// but sizes are bounded and empty entries removed.
    /// </summary>
    public static CvDocument NormalizeUserEdit(CvDocument cv)
    {
        ArgumentNullException.ThrowIfNull(cv);
        return new CvDocument
        {
            Headline = Clip(cv.Headline, 140),
            Summary = Clip(cv.Summary, 3000),
            SkillGroups = (cv.SkillGroups ?? [])
                .Where(group => group is not null)
                .Select(group => new CvSkillGroup
                {
                    Category = Clip(group.Category, 60),
                    Skills = CleanList(group.Skills).Take(60).ToList()
                })
                .Where(group => group.Skills.Count > 0)
                .Take(12)
                .ToList(),
            Experience = (cv.Experience ?? [])
                .Where(entry => entry is not null)
                .Select(entry => new CvExperience
                {
                    SourceIndex = entry.SourceIndex,
                    Role = Clip(entry.Role, 160),
                    Period = Clip(entry.Period, 120),
                    Bullets = CleanList(entry.Bullets).Select(bullet => Clip(bullet, 1000)).Take(15).ToList()
                })
                .Where(entry => entry.Role.Length > 0 || entry.Bullets.Count > 0)
                .Take(50)
                .ToList(),
            Education = CleanList(cv.Education).Take(30).ToList(),
            Languages = CleanList(cv.Languages).Take(30).ToList(),
            Certifications = CleanList(cv.Certifications).Take(50).ToList()
        };
    }

    private static CvExperience FromProfile(int index, ExperienceEntry source, List<string> bullets) => new()
    {
        SourceIndex = index,
        Role = source.Role?.Trim() ?? string.Empty,
        Period = source.Period?.Trim() ?? string.Empty,
        Bullets = bullets
    };

    private static List<string> CleanBullets(IEnumerable<string>? bullets) =>
        CleanList(bullets)
            .Select(bullet => Clip(bullet.TrimStart('•', '-', '*', ' '), MaxBulletLength))
            .Where(bullet => bullet.Length > 0)
            .Take(MaxBullets)
            .ToList();

    private static List<string> CleanList(IEnumerable<string>? values) =>
        (values ?? []).Select(value => value?.Trim() ?? string.Empty).Where(value => value.Length > 0).ToList();

    private static string Clip(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }
}
