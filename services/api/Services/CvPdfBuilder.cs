using JobPilot.Api.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace JobPilot.Api.Services;

/// <summary>
/// Renders a generated CV as a single-column, ATS-friendly A4 PDF.
/// </summary>
public static class CvPdfBuilder
{
    private const string Accent = "#1F3A93";
    private const string Muted = "#555555";
    private const string Body = "#1F2733";

    static CvPdfBuilder()
    {
        // Free for individuals and companies under the Community licence threshold.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Build(ContactDetails contact, CvDocument cv)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(cv);

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(45);
                page.MarginVertical(40);
                page.DefaultTextStyle(style => style.FontSize(10).FontColor(Body).LineHeight(1.3f));

                page.Content().Column(column =>
                {
                    column.Spacing(4);
                    ComposeHeader(column, contact, cv);

                    if (!string.IsNullOrWhiteSpace(cv.Summary))
                    {
                        Section(column, "Profile");
                        column.Item().Text(cv.Summary);
                    }

                    if (cv.SkillGroups.Count > 0)
                    {
                        Section(column, "Skills");
                        foreach (var group in cv.SkillGroups)
                        {
                            column.Item().Row(row =>
                            {
                                if (!string.IsNullOrWhiteSpace(group.Category))
                                {
                                    row.ConstantItem(120).Text(group.Category).SemiBold();
                                }

                                row.RelativeItem().Text(string.Join(", ", group.Skills));
                            });
                        }
                    }

                    if (cv.Experience.Count > 0)
                    {
                        Section(column, "Professional experience");
                        foreach (var role in cv.Experience)
                        {
                            column.Item().PaddingTop(4).EnsureSpace(60).Column(entry =>
                            {
                                entry.Item().Row(row =>
                                {
                                    row.RelativeItem().Text(role.Role).SemiBold();
                                    if (!string.IsNullOrWhiteSpace(role.Period))
                                    {
                                        row.AutoItem().PaddingLeft(10).Text(role.Period).FontColor(Muted);
                                    }
                                });
                                Bullets(entry, role.Bullets);
                            });
                        }
                    }

                    if (cv.Projects.Count > 0)
                    {
                        Section(column, "Selected projects");
                        foreach (var project in cv.Projects)
                        {
                            column.Item().PaddingTop(4).EnsureSpace(60).Column(entry =>
                            {
                                entry.Item().Row(row =>
                                {
                                    row.RelativeItem().Text(text =>
                                    {
                                        text.Span(project.Name).SemiBold();
                                        if (!string.IsNullOrWhiteSpace(project.Context))
                                        {
                                            text.Span($"  ·  {project.Context}").FontColor(Muted);
                                        }
                                    });
                                    if (!string.IsNullOrWhiteSpace(project.Url))
                                    {
                                        row.AutoItem().PaddingLeft(10).Hyperlink(ToAbsoluteUrl(project.Url))
                                            .Text(project.Url).FontColor(Accent).FontSize(9);
                                    }
                                });
                                Bullets(entry, project.Bullets);
                                if (project.Technologies.Count > 0)
                                {
                                    entry.Item().Text(string.Join(", ", project.Technologies)).Italic().FontColor(Muted).FontSize(9);
                                }
                            });
                        }
                    }

                    ListSection(column, "Education", cv.Education);
                    ListSection(column, "Certifications", cv.Certifications);
                    if (cv.Languages.Count > 0)
                    {
                        Section(column, "Languages");
                        column.Item().Text(string.Join("  ·  ", cv.Languages));
                    }
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(ColumnDescriptor column, ContactDetails contact, CvDocument cv)
    {
        if (!string.IsNullOrWhiteSpace(contact.FullName))
        {
            column.Item().Text(contact.FullName).FontSize(22).Bold();
        }

        if (!string.IsNullOrWhiteSpace(cv.Headline))
        {
            column.Item().Text(cv.Headline).FontSize(12).SemiBold().FontColor(Accent);
        }

        var parts = new[] { contact.Location, contact.Email, contact.Phone, contact.LinkedIn, contact.Website }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();
        if (parts.Count > 0)
        {
            column.Item().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(9).FontColor(Muted));
                for (var index = 0; index < parts.Count; index++)
                {
                    if (index > 0)
                    {
                        text.Span("  ·  ");
                    }

                    var link = ContactLink(parts[index]);
                    if (link is null)
                    {
                        text.Span(parts[index]);
                    }
                    else
                    {
                        text.Hyperlink(parts[index], link).FontColor(Accent);
                    }
                }
            });
        }
    }

    private static void Section(ColumnDescriptor column, string title)
    {
        column.Item().PaddingTop(10).BorderBottom(1).BorderColor(Accent).PaddingBottom(2)
            .Text(title.ToUpperInvariant()).FontSize(10.5f).Bold().FontColor(Accent).LetterSpacing(0.06f);
    }

    private static void Bullets(ColumnDescriptor column, IEnumerable<string> bullets)
    {
        foreach (var bullet in bullets)
        {
            column.Item().PaddingLeft(4).Row(row =>
            {
                row.ConstantItem(10).Text("•");
                row.RelativeItem().Text(bullet);
            });
        }
    }

    private static void ListSection(ColumnDescriptor column, string title, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        Section(column, title);
        Bullets(column, items);
    }

    private static string? ContactLink(string value)
    {
        if (value.Contains('@') && !value.Contains(' '))
        {
            return $"mailto:{value}";
        }

        return value.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? ToAbsoluteUrl(value)
            : null;
    }

    private static string ToAbsoluteUrl(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"https://{value}";
}
