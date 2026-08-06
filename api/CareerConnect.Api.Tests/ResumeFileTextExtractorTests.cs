using CareerConnect.Api.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CareerConnect.Api.Tests;

public sealed class ResumeFileTextExtractorTests
{
    private readonly ResumeFileTextExtractor _extractor = new();

    [Fact]
    public void Extract_ReturnsParagraphTextForDocx()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Jane Doe"))),
                new Paragraph(new Run(new Text("Software Engineer with 5 years of experience.")))));
            mainPart.Document.Save();
        }
        stream.Position = 0;

        var text = _extractor.Extract(stream, "resume.docx");

        Assert.NotNull(text);
        Assert.Contains("Jane Doe", text);
        Assert.Contains("Software Engineer with 5 years of experience.", text);
    }

    [Fact]
    public void Extract_ExtractsParagraphsNestedInTables()
    {
        // Many resume templates lay sections out in a table for column
        // layout — this is the case Descendants<Paragraph>() exists to catch.
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var table = new Table(
                new TableRow(
                    new TableCell(new Paragraph(new Run(new Text("Skills: C#, React"))))));
            mainPart.Document = new Document(new Body(table));
            mainPart.Document.Save();
        }
        stream.Position = 0;

        var text = _extractor.Extract(stream, "resume.docx");

        Assert.NotNull(text);
        Assert.Contains("Skills: C#, React", text);
    }

    [Fact]
    public void Extract_ReturnsNullForUnsupportedExtension()
    {
        using var stream = new MemoryStream("plain text resume"u8.ToArray());

        Assert.Null(_extractor.Extract(stream, "resume.txt"));
    }
}
