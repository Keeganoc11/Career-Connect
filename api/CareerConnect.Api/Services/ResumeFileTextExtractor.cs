using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace CareerConnect.Api.Services;

/// <summary>
/// Extracts plain text from uploaded resume files so they're treated the same
/// as pasted text everywhere downstream (storage, scoring). No OCR — a
/// scanned/image-only PDF yields little or no text, which the caller should
/// treat as a failure rather than silently storing an empty resume.
/// </summary>
public class ResumeFileTextExtractor : IResumeFileTextExtractor
{
    public string? Extract(Stream fileStream, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".docx" => ExtractDocx(fileStream),
            ".pdf" => ExtractPdf(fileStream),
            _ => null,
        };
    }

    private static string ExtractDocx(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return "";
        }

        // Descendants, not Elements: many resume templates lay sections out in
        // tables, and Elements<Paragraph>() only sees the body's direct children.
        var paragraphs = body.Descendants<Paragraph>().Select(p => p.InnerText);
        return string.Join('\n', paragraphs);
    }

    private static string ExtractPdf(Stream stream)
    {
        using var document = PdfDocument.Open(stream);
        var pages = document.GetPages().Select(page => page.Text);
        return string.Join("\n\n", pages);
    }
}
