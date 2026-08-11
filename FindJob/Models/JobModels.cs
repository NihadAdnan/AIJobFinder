using System.Text.Json.Serialization;

namespace FindJob.Models;

public class JobData
{
    public string JobId { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
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

/// <summary>
/// Common fields observed in Bdjobs JSON responses.
/// </summary>
public class BdjobsApiResponse
{
    [JsonPropertyName("jobId")]
    public object? JobId { get; set; }

    [JsonPropertyName("jobTitle")]
    public string? JobTitle { get; set; }

    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("companyAddress")]
    public string? CompanyAddress { get; set; }

    [JsonPropertyName("jobLoc")]
    public string? JobLoc { get; set; }

    [JsonPropertyName("jobNature")]
    public string? JobNature { get; set; }

    [JsonPropertyName("workplace")]
    public string? Workplace { get; set; }

    [JsonPropertyName("jobContext")]
    public string? JobContext { get; set; }

    [JsonPropertyName("jobResp")]
    public string? JobResp { get; set; }

    [JsonPropertyName("eduReq")]
    public string? EduReq { get; set; }

    [JsonPropertyName("expReq")]
    public string? ExpReq { get; set; }

    [JsonPropertyName("minExp")]
    public object? MinExp { get; set; }

    [JsonPropertyName("maxExp")]
    public object? MaxExp { get; set; }

    [JsonPropertyName("addReq")]
    public string? AddReq { get; set; }

    [JsonPropertyName("skills")]
    public string? Skills { get; set; }

    [JsonPropertyName("salary")]
    public string? Salary { get; set; }

    [JsonPropertyName("minSalary")]
    public object? MinSalary { get; set; }

    [JsonPropertyName("maxSalary")]
    public object? MaxSalary { get; set; }

    [JsonPropertyName("otherBenifits")]
    public string? OtherBenefits { get; set; }

    [JsonPropertyName("deadline")]
    public string? Deadline { get; set; }

    [JsonPropertyName("publishDate")]
    public string? PublishDate { get; set; }
}
