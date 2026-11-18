using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Files;
using Gggs.Data;
using Gggs.Modules.Recruitment.Domain;
using Gggs.Modules.Recruitment.Infrastructure;
using Gggs.Modules.Skills.Contracts;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Gggs.Modules.Recruitment.Application;

/// <summary>
/// A CV read for the Add a candidate form: the fields it gave, and the stored file to attach when the
/// candidate is saved. <c>HasText</c> is false for a scanned CV: there is nothing to read, but the file
/// is still kept.
/// </summary>
public sealed record CvReadResult(string CvKey, string FileName, bool HasText, int Found, CvFields Fields);

/// <summary>A CV read earlier, to attach to a candidate that already exists.</summary>
public sealed record AttachCvBody(string CvKey, string FileName);

/// <summary>
/// Takes the text out of a CV on our own server - PDF with PdfPig (Apache-2.0), Word by reading the
/// .docx's own XML, plain text as it is - so nothing goes to an outside service and nothing is paid per CV.
/// </summary>
public static class CvText
{
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>The first pages are enough: contact details, latest jobs and skills are at the front.</summary>
    private const int MaxPages = 6;

    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static bool IsSupported(string fileName) => Kind(fileName) is not null;

    public static string Extract(Stream content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Kind(fileName) switch
        {
            ".pdf" => Pdf(content),
            ".docx" => Docx(content),
            ".txt" => new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true).ReadToEnd(),
            _ => throw new DomainRuleException("CV_FILE_TYPE", "Choose a PDF, Word (.docx) or text file."),
        };
    }

    private static string? Kind(string fileName)
    {
        string extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return extension is ".pdf" or ".docx" or ".txt" ? extension : null;
    }

    /// <summary>Words grouped back into lines by their height on the page, left to right.</summary>
    private static string Pdf(Stream content)
    {
        try
        {
            using PdfDocument document = PdfDocument.Open(content);
            var text = new StringBuilder();
            foreach (Page page in document.GetPages().Take(MaxPages))
            {
                var words = page.GetWords().Where(word => !string.IsNullOrWhiteSpace(word.Text)).OrderByDescending(word => word.BoundingBox.Bottom).ToList();
                var line = new List<UglyToad.PdfPig.Content.Word>();
                double? lineBottom = null;
                foreach (UglyToad.PdfPig.Content.Word word in words)
                {
                    double tolerance = Math.Max(2, word.BoundingBox.Height * 0.5);
                    if (lineBottom is { } bottom && Math.Abs(word.BoundingBox.Bottom - bottom) > tolerance)
                    {
                        text.AppendLine(string.Join(' ', line.OrderBy(item => item.BoundingBox.Left).Select(item => item.Text)));
                        line.Clear();
                    }

                    line.Add(word);
                    lineBottom = word.BoundingBox.Bottom;
                }

                if (line.Count > 0)
                {
                    text.AppendLine(string.Join(' ', line.OrderBy(item => item.BoundingBox.Left).Select(item => item.Text)));
                }
            }

            return text.ToString();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not DomainRuleException)
        {
            throw new DomainRuleException("CV_UNREADABLE", "This PDF could not be read. It may be damaged or password-protected.");
        }
    }

    /// <summary>A .docx is a zip whose word/document.xml holds the text: one paragraph per line.</summary>
    private static string Docx(Stream content)
    {
        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read);
            ZipArchiveEntry entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("Not a Word document.");
            if (entry.Length > 20 * 1024 * 1024)
            {
                throw new InvalidDataException("Too large.");
            }

            using Stream xml = entry.Open();
            XDocument document = XDocument.Load(xml);
            var text = new StringBuilder();
            foreach (XElement paragraph in document.Descendants(Word + "p"))
            {
                foreach (XElement node in paragraph.Descendants())
                {
                    if (node.Name == Word + "t")
                    {
                        text.Append(node.Value);
                    }
                    else if (node.Name == Word + "tab")
                    {
                        text.Append(' ');
                    }
                    else if (node.Name == Word + "br")
                    {
                        text.AppendLine();
                    }
                }

                text.AppendLine();
            }

            return text.ToString();
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Xml.XmlException or IOException)
        {
            throw new DomainRuleException("CV_UNREADABLE", "This Word file could not be read. Save it as .docx or PDF and try again.");
        }
    }
}

/// <summary>
/// Reads a CV for the Add a candidate form and keeps the file, so the candidate saved from that form
/// carries the original. Skills are matched to the Skills module's names and to those used on requirements.
/// </summary>
internal sealed class CvService(AppDbContext dbContext, IFileStorage files, ISkillDirectory skills, TimeProvider timeProvider)
{
    private const string KeyPrefix = "recruitment/cv/";

    public async Task<CvReadResult> ReadAsync(Stream content, string fileName, string? contentType, long length, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        string name = SafeName(fileName);
        if (!CvText.IsSupported(name))
        {
            throw new DomainRuleException("CV_FILE_TYPE", "Choose a PDF, Word (.docx) or text file.");
        }

        if (length <= 0 || length > CvText.MaxBytes)
        {
            throw new DomainRuleException("CV_FILE_SIZE", "The CV must be smaller than 5 MB.");
        }

        // One copy in memory: the reader needs to seek, and the same bytes go to storage.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        string text = CvText.Extract(buffer, name);

        buffer.Position = 0;
        StoredFile stored = await files.SaveAsync(
            new FileToStore { Content = buffer, RelativeKey = $"{KeyPrefix}{Guid.NewGuid():N}/{name}", ContentType = ContentTypeOf(name, contentType) },
            cancellationToken);

        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        CvFields fields = CvParser.Parse(text, await KnownSkillsAsync(cancellationToken), [], today);
        return new CvReadResult(stored.StorageKey, name, text.Trim().Length > 20, fields.Found, fields);
    }

    /// <summary>Puts a CV read earlier on a candidate; the key must be one this feature stored.</summary>
    public async Task AttachAsync(Candidate candidate, string? cvKey, string? fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (string.IsNullOrWhiteSpace(cvKey))
        {
            return;
        }

        if (!IsCvKey(cvKey) || !await files.ExistsAsync(cvKey, cancellationToken))
        {
            throw new DomainRuleException("CV_MISSING", "The CV could not be found. Choose the file again.");
        }

        candidate.AttachCv(cvKey, SafeName(string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(cvKey) : fileName));
    }

    public Task<Stream> OpenAsync(Candidate candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate.CvStorageKey is { } key
            ? files.OpenReadAsync(key, cancellationToken)
            : throw new DomainRuleException("CV_NONE", "No CV is attached to this candidate.");
    }

    public static string ContentTypeOf(string fileName, string? given = null) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".txt" => "text/plain",
        _ => string.IsNullOrWhiteSpace(given) ? "application/octet-stream" : given,
    };

    /// <summary>The storage adapter adds the tenant to the key it returns; a key from this feature still names its folder.</summary>
    private static bool IsCvKey(string key)
        => key.Contains(KeyPrefix, StringComparison.Ordinal) && !key.Contains("..", StringComparison.Ordinal);

    /// <summary>Just the file name, without any path a browser might send, and without characters storage dislikes.</summary>
    private static string SafeName(string? fileName)
    {
        string name = Path.GetFileName((fileName ?? "cv").Replace('\\', '/'));
        var clean = new string([.. name.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' or ' ' ? ch : '_')]).Trim();
        return clean.Length == 0 ? "cv" : clean.Length > 120 ? clean[^120..] : clean;
    }

    private async Task<IReadOnlyList<string>> KnownSkillsAsync(CancellationToken cancellationToken)
    {
        List<string> fromRequirements = (await dbContext.Requisitions.AsNoTracking().Select(row => row.SkillsJson).Take(500).ToListAsync(cancellationToken))
            .SelectMany(json => System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [])
            .ToList();
        return [.. (await skills.ActiveSkillNamesAsync(cancellationToken)).Concat(fromRequirements).Where(skill => skill.Trim().Length >= 2).Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
