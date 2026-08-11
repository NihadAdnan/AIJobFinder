namespace FindJob.Models;

public class TextChunk
{
    public string Text { get; set; } = string.Empty;
    public string Section { get; set; } = "General";
    public float[]? Vector { get; set; }

    public TextChunk() { }

    public TextChunk(string text, string section = "General")
    {
        Text = text;
        Section = section;
    }
}

public class ResumeData
{
    public string RawText { get; set; } = string.Empty;
    public string? CandidateName { get; set; }
    public List<TextChunk> Chunks { get; set; } = new();
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
