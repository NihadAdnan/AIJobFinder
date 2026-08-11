using System.Text.Json.Serialization;

namespace FindJob.Models;

public class JobData
{
    public string JobId { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string SourceDomain { get; set; } = "Web";
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Experience { get; set; } = string.Empty;
    public string Salary { get; set; } = string.Empty;
    public string Deadline { get; set; } = string.Empty;
    public string JobNature { get; set; } = string.Empty;
    public string Workplace { get; set; } = string.Empty;

    public string Responsibilities { get; set; } = string.Empty;
    public string Requirements { get; set; } = string.Empty;
    public string AdditionalRequirements { get; set; } = string.Empty;
    public string Education { get; set; } = string.Empty;
    public string OtherBenefits { get; set; } = string.Empty;

    public string RawFlattenedText { get; set; } = string.Empty;
    public List<TextChunk> Chunks { get; set; } = new();

    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Formats the job posting into structured, clean text for LLM comparison.
    /// </summary>
    public string ToFormattedContext()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Title)) parts.Add($"Job Title: {Title}");
        if (!string.IsNullOrWhiteSpace(Company)) parts.Add($"Company: {Company}");
        if (!string.IsNullOrWhiteSpace(Location)) parts.Add($"Location: {Location}");
        if (!string.IsNullOrWhiteSpace(JobNature)) parts.Add($"Employment Type: {JobNature}");
        if (!string.IsNullOrWhiteSpace(Experience)) parts.Add($"Experience Required: {Experience}");
        if (!string.IsNullOrWhiteSpace(Salary)) parts.Add($"Salary Range: {Salary}");
        if (!string.IsNullOrWhiteSpace(Education)) parts.Add($"Education Requirements: {Education}");
        if (!string.IsNullOrWhiteSpace(Requirements)) parts.Add($"Requirements / Skills: {Requirements}");
        if (!string.IsNullOrWhiteSpace(Responsibilities)) parts.Add($"Responsibilities: {Responsibilities}");
        if (!string.IsNullOrWhiteSpace(AdditionalRequirements)) parts.Add($"Additional Requirements: {AdditionalRequirements}");
        if (!string.IsNullOrWhiteSpace(OtherBenefits)) parts.Add($"Benefits: {OtherBenefits}");

        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(RawFlattenedText))
        {
            return RawFlattenedText;
        }

        return string.Join("\n\n", parts);
    }
}
