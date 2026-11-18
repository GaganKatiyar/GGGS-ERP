using System.IO.Compression;
using System.Text;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Modules.Recruitment.Application;
using Shouldly;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Gggs.Hiring.UnitTests;

/// <summary>Taking the text out of a CV file, on our own server: PDF, Word and plain text, line by line.</summary>
public sealed class CvTextTests
{
    [Fact]
    public void A_PDF_comes_back_line_by_line_in_reading_order()
    {
        var builder = new PdfDocumentBuilder();
        PdfPageBuilder page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("Riya Sharma", 18, new UglyToad.PdfPig.Core.PdfPoint(50, 780), font);
        page.AddText("riya.sharma@example.com | +91 98765 43210", 10, new UglyToad.PdfPig.Core.PdfPoint(50, 760), font);
        page.AddText("Infosys Ltd - Senior QA Engineer", 10, new UglyToad.PdfPig.Core.PdfPoint(50, 720), font);

        string text = CvText.Extract(new MemoryStream(builder.Build()), "riya.pdf");
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        lines[0].ShouldBe("Riya Sharma");
        lines[1].ShouldContain("riya.sharma@example.com");
        lines[2].ShouldBe("Infosys Ltd - Senior QA Engineer");
    }

    [Fact]
    public void A_Word_file_comes_back_one_paragraph_per_line()
    {
        string text = CvText.Extract(Docx("Arjun Nair", "arjun@example.com", "Skills: C#, .NET"), "arjun.docx");

        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ShouldBe(["Arjun Nair", "arjun@example.com", "Skills: C#, .NET"]);
    }

    [Fact]
    public void Plain_text_is_read_as_it_is()
        => CvText.Extract(new MemoryStream(Encoding.UTF8.GetBytes("Meera Iyer\nmeera@example.com")), "meera.txt").ShouldContain("meera@example.com");

    [Theory]
    [InlineData("old.doc")]
    [InlineData("photo.jpg")]
    public void Other_file_types_are_refused_with_a_reason(string fileName)
    {
        CvText.IsSupported(fileName).ShouldBeFalse();
        Should.Throw<DomainRuleException>(() => CvText.Extract(new MemoryStream([1, 2, 3]), fileName)).ErrorCode.ShouldBe("CV_FILE_TYPE");
    }

    [Fact]
    public void A_damaged_PDF_says_so_rather_than_failing()
        => Should.Throw<DomainRuleException>(() => CvText.Extract(new MemoryStream(Encoding.ASCII.GetBytes("not really a pdf")), "broken.pdf")).ErrorCode.ShouldBe("CV_UNREADABLE");

    /// <summary>The smallest .docx a reader accepts: the zip with word/document.xml.</summary>
    private static MemoryStream Docx(params string[] paragraphs)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
            foreach (string paragraph in paragraphs)
            {
                writer.Write($"<w:p><w:r><w:t>{System.Security.SecurityElement.Escape(paragraph)}</w:t></w:r></w:p>");
            }

            writer.Write("</w:body></w:document>");
        }

        stream.Position = 0;
        return stream;
    }
}
