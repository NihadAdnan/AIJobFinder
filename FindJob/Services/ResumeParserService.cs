using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FindJob.Models;
using Microsoft.AspNetCore.Http;
using UglyToad.PdfPig;

namespace FindJob.Services;

public partial class ResumeParserService : IResumeParserService
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private static readonly string[] RecognizedSections =
    {
        "SUMMARY", "PROFESSIONAL SUMMARY", "EXECUTIVE SUMMARY", "OBJECTIVE", "CAREER OBJECTIVE",
        "SKILLS", "TECHNICAL SKILLS", "CORE COMPETENCIES", "KEY SKILLS", "TECHNOLOGIES",
        "EXPERIENCE", "WORK EXPERIENCE", "EMPLOYMENT HISTORY", "PROFESSIONAL EXPERIENCE", "WORK HISTORY",
        "PROJECTS", "KEY PROJECTS", "PERSONAL PROJECTS", "ACADEMIC PROJECTS",
        "EDUCATION", "ACADEMIC BACKGROUND", "QUALIFICATIONS",
        "CERTIFICATIONS", "LICENSES & CERTIFICATIONS", "COURSES",
        "ACHIEVEMENTS", "HONORS & AWARDS", "AWARDS", "PUBLICATIONS",
        "LANGUAGES", "REFERENCES"
    };

    public async Task<ResumeData> ParseResumeAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var result = new ResumeData();

        if (file == null || file.Length == 0)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "No resume file was uploaded.";
            return result;
        }

        if (file.Length > MaxFileSizeBytes)
        {
            result.IsSuccess = false;
            result.ErrorMessage = $"Uploaded file exceeds the maximum allowed size of 5 MB ({file.Length / 1024 / 1024:F1} MB).";
            return result;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        string rawText = string.Empty;

        try
        {
            await using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            if (extension == ".pdf")
            {
                rawText = ExtractTextFromPdf(memoryStream);
            }
            else if (extension == ".docx")
            {
                rawText = ExtractTextFromDocx(memoryStream);
            }
            else if (extension == ".txt")
            {
                using var reader = new StreamReader(memoryStream, Encoding.UTF8);
                rawText = await reader.ReadToEndAsync(cancellationToken);
            }
            else
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"Unsupported file format '{extension}'. Please upload a PDF, DOCX, or TXT file.";
                return result;
            }

            rawText = CleanExtractedText(rawText);

            if (string.IsNullOrWhiteSpace(rawText) || rawText.Length < 30)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Scanned or image-only PDF detected (no readable text found). Please upload a text-searchable PDF or Word document.";
                return result;
            }

            result.RawText = rawText;
            result.Chunks = ChunkResumeText(rawText);
            result.CandidateName = ExtractCandidateName(rawText);
            result.IsSuccess = true;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = $"Failed to parse resume: {ex.Message}";
        }

        return result;
    }

    public ResumeData ParseText(string text, string fileName = "sample_resume.txt")
    {
        var cleaned = CleanExtractedText(text);
        return new ResumeData
        {
            RawText = cleaned,
            Chunks = ChunkResumeText(cleaned),
            CandidateName = ExtractCandidateName(cleaned),
            IsSuccess = !string.IsNullOrWhiteSpace(cleaned)
        };
    }

    private static string ExtractTextFromPdf(Stream stream)
    {
        var sb = new StringBuilder();
        using var pdf = PdfDocument.Open(stream);

        foreach (var page in pdf.GetPages())
        {
            var pageText = page.Text;
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                sb.AppendLine(pageText);
            }
        }

        return sb.ToString();
    }

    private static string ExtractTextFromDocx(Stream stream)
    {
        var sb = new StringBuilder();
        using var wordDoc = WordprocessingDocument.Open(stream, false);
        var body = wordDoc.MainDocumentPart?.Document?.Body;

        if (body != null)
        {
            foreach (var element in body.Elements())
            {
                if (element is Paragraph p)
                {
                    var text = p.InnerText;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text);
                    }
                }
                else if (element is Table table)
                {
                    foreach (var row in table.Elements<TableRow>())
                    {
                        var rowTexts = row.Elements<TableCell>().Select(c => c.InnerText.Trim()).Where(s => !string.IsNullOrEmpty(s));
                        sb.AppendLine(string.Join(" | ", rowTexts));
                    }
                }
            }
        }

        return sb.ToString();
    }

    private static string CleanExtractedText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // Normalize unicode whitespace and line breaks
        var cleaned = input.Replace("\r\n", "\n").Replace("\r", "\n");
        // Remove excessive empty lines
        cleaned = MultipleNewlinesRegex().Replace(cleaned, "\n\n");
        // Remove weird control characters
        cleaned = ControlCharsRegex().Replace(cleaned, " ");
        // Collapse multiple spaces within a line
        cleaned = MultipleSpacesRegex().Replace(cleaned, " ");

        return cleaned.Trim();
    }

    private static string? ExtractCandidateName(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 0)
        {
            var firstLine = lines[0];
            if (firstLine.Length <= 50 && !firstLine.Contains(':') && !firstLine.Contains('@') && !firstLine.Contains("http"))
            {
                return firstLine;
            }
        }
        return null;
    }

    private static List<TextChunk> ChunkResumeText(string rawText)
    {
        var chunks = new List<TextChunk>();
        var lines = rawText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string currentSection = "Profile / Summary";
        var currentSectionBuffer = new StringBuilder();

        foreach (var line in lines)
        {
            // Check if line looks like a section header
            var upperLine = line.ToUpperInvariant().Trim(' ', ':', '-', '#', '*');
            var matchedSection = RecognizedSections.FirstOrDefault(s => upperLine == s || upperLine.StartsWith(s + ":") || upperLine.StartsWith(s + " -"));

            if (!string.IsNullOrEmpty(matchedSection))
            {
                // Save previous section buffer if any
                FlushSectionBuffer(chunks, currentSection, currentSectionBuffer);
                currentSection = matchedSection;
                currentSectionBuffer.Clear();
            }
            else
            {
                currentSectionBuffer.AppendLine(line);
            }
        }

        // Flush remaining buffer
        FlushSectionBuffer(chunks, currentSection, currentSectionBuffer);

        // Fallback: If section chunking resulted in too few chunks or empty, do paragraph-based chunking
        if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(rawText))
        {
            var paragraphs = rawText.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in paragraphs)
            {
                if (p.Length > 30)
                {
                    chunks.Add(new TextChunk(p, "General"));
                }
            }
        }

        return chunks;
    }

    private static void FlushSectionBuffer(List<TextChunk> chunks, string section, StringBuilder buffer)
    {
        var text = buffer.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        // If section is long (> 800 chars), split into sub-chunks of ~500 chars with some overlap
        if (text.Length > 800)
        {
            var paragraphs = text.Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var subBuffer = new StringBuilder();

            foreach (var p in paragraphs)
            {
                if (subBuffer.Length + p.Length > 600 && subBuffer.Length > 0)
                {
                    chunks.Add(new TextChunk(subBuffer.ToString().Trim(), section));
                    subBuffer.Clear();
                }
                subBuffer.AppendLine(p);
            }

            if (subBuffer.Length > 0)
            {
                chunks.Add(new TextChunk(subBuffer.ToString().Trim(), section));
            }
        }
        else if (text.Length >= 20)
        {
            chunks.Add(new TextChunk(text, section));
        }
    }

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleNewlinesRegex();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F]")]
    private static partial Regex ControlCharsRegex();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultipleSpacesRegex();
}
