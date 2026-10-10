using System.IO.Compression;
using System.Security;
using System.Text;
using JobPilot.Api.Models;

namespace JobPilot.Api.Services;

/// <summary>
/// Writes a CV as a minimal Office Open XML (.docx) package without third-party dependencies.
/// </summary>
public static class CvDocxBuilder
{
    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>
        """;

    private const string RootRelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    private const string AccentColor = "1F3A93";

    public static byte[] Build(ContactDetails contact, CvDocument cv)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(cv);

        var body = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(contact.FullName))
        {
            body.Append(Paragraph(Run(contact.FullName, bold: true, sizeHalfPoints: 40, color: AccentColor), spacingAfter: 40));
        }

        if (!string.IsNullOrWhiteSpace(cv.Headline))
        {
            body.Append(Paragraph(Run(cv.Headline, sizeHalfPoints: 24, color: "444444"), spacingAfter: 60));
        }

        var contactLine = string.Join("  |  ", new[]
            {
                contact.Email, contact.Phone, contact.Location, contact.LinkedIn, contact.Website
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));
        if (contactLine.Length > 0)
        {
            body.Append(Paragraph(Run(contactLine, sizeHalfPoints: 18, color: "555555"), spacingAfter: 120));
        }

        if (!string.IsNullOrWhiteSpace(cv.Summary))
        {
            body.Append(Heading("Profile"));
            body.Append(Paragraph(Run(cv.Summary)));
        }

        if (cv.SkillGroups.Count > 0)
        {
            body.Append(Heading("Skills"));
            foreach (var group in cv.SkillGroups)
            {
                var label = string.IsNullOrWhiteSpace(group.Category) ? string.Empty : $"{group.Category}: ";
                body.Append(Paragraph(Run(label, bold: true) + Run(string.Join(", ", group.Skills)), spacingAfter: 40));
            }
        }

        if (cv.Experience.Count > 0)
        {
            body.Append(Heading("Experience"));
            foreach (var role in cv.Experience)
            {
                var period = string.IsNullOrWhiteSpace(role.Period) ? string.Empty : Run($"  ·  {role.Period}", color: "555555");
                body.Append(Paragraph(Run(role.Role, bold: true) + period, spacingAfter: 40, keepNext: true));
                foreach (var bullet in role.Bullets)
                {
                    body.Append(Bullet(bullet));
                }
            }
        }

        if (cv.Projects.Count > 0)
        {
            body.Append(Heading("Selected projects"));
            foreach (var project in cv.Projects)
            {
                var context = string.IsNullOrWhiteSpace(project.Context) ? string.Empty : Run($"  ·  {project.Context}", color: "555555");
                body.Append(Paragraph(Run(project.Name, bold: true) + context, spacingAfter: 20, keepNext: true));
                if (!string.IsNullOrWhiteSpace(project.Url))
                {
                    body.Append(Paragraph(Run(project.Url, sizeHalfPoints: 18, color: AccentColor), spacingAfter: 40, keepNext: true));
                }

                foreach (var bullet in project.Bullets)
                {
                    body.Append(Bullet(bullet));
                }

                if (project.Technologies.Count > 0)
                {
                    body.Append(Paragraph(Run(string.Join(", ", project.Technologies), sizeHalfPoints: 18, color: "555555"), spacingAfter: 100));
                }
            }
        }

        AppendList(body, "Education", cv.Education);
        AppendList(body, "Certifications", cv.Certifications);
        AppendList(body, "Languages", cv.Languages);

        var documentXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                {body}
                <w:sectPr>
                  <w:pgSz w:w="11906" w:h="16838"/>
                  <w:pgMar w:top="1000" w:right="1000" w:bottom="1000" w:left="1000" w:header="600" w:footer="600" w:gutter="0"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """;

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", ContentTypesXml);
            AddEntry(archive, "_rels/.rels", RootRelsXml);
            AddEntry(archive, "word/document.xml", documentXml);
        }

        return stream.ToArray();
    }

    private static void AppendList(StringBuilder body, string heading, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        body.Append(Heading(heading));
        foreach (var item in items)
        {
            body.Append(Bullet(item));
        }
    }

    private static string Heading(string text) =>
        "<w:p><w:pPr><w:keepNext/><w:spacing w:before=\"220\" w:after=\"80\"/>" +
        $"<w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"1\" w:color=\"{AccentColor}\"/></w:pBdr></w:pPr>" +
        Run(text.ToUpperInvariant(), bold: true, sizeHalfPoints: 22, color: AccentColor) +
        "</w:p>";

    private static string Bullet(string text) =>
        "<w:p><w:pPr><w:spacing w:after=\"40\"/><w:ind w:left=\"360\" w:hanging=\"220\"/></w:pPr>" +
        Run("•\t") + Run(text) +
        "</w:p>";

    private static string Paragraph(string runs, int spacingAfter = 100, bool keepNext = false) =>
        $"<w:p><w:pPr>{(keepNext ? "<w:keepNext/>" : string.Empty)}<w:spacing w:after=\"{spacingAfter}\"/></w:pPr>{runs}</w:p>";

    private static string Run(string text, bool bold = false, int sizeHalfPoints = 20, string? color = null)
    {
        var properties = new StringBuilder("<w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Calibri\"/>");
        if (bold)
        {
            properties.Append("<w:b/>");
        }

        if (color is not null)
        {
            properties.Append($"<w:color w:val=\"{color}\"/>");
        }

        properties.Append($"<w:sz w:val=\"{sizeHalfPoints}\"/><w:szCs w:val=\"{sizeHalfPoints}\"/></w:rPr>");

        var escaped = SecurityElement.Escape(RemoveInvalidXmlChars(text)) ?? string.Empty;
        var content = escaped.Contains('\t')
            ? string.Join("<w:tab/>", escaped.Split('\t').Select(part => $"<w:t xml:space=\"preserve\">{part}</w:t>"))
            : $"<w:t xml:space=\"preserve\">{escaped}</w:t>";
        return $"<w:r>{properties}{content}</w:r>";
    }

    private static string RemoveInvalidXmlChars(string text) =>
        new(text.Where(character => character == '\t' || character == '\n' || character == '\r' || character >= ' ').ToArray());

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content.Trim());
    }
}
