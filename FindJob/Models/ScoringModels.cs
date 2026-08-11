using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace FindJob.Models;

public class ScoringResult
{
    public string JobId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public int Score { get; set; } // 0 - 100
    public List<string> MatchedSkills { get; set; } = new();
    public List<string> Gaps { get; set; } = new();
    public string Reasoning { get; set; } = string.Empty;
    public List<string> KeyStrengths { get; set; } = new();
    public float TopCosineSimilarity { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? Location { get; set; }
    public string? Salary { get; set; }
    public string? Experience { get; set; }
}

public class LlmScoreOutput
{
    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("matchedSkills")]
    public List<string> MatchedSkills { get; set; } = new();

    [JsonPropertyName("gaps")]
    public List<string> Gaps { get; set; } = new();

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;

    [JsonPropertyName("keyStrengths")]
    public List<string>? KeyStrengths { get; set; }
}

public class JobFinderRequestViewModel
{
    public IFormFile? ResumeFile { get; set; }

    public List<string> JobUrls { get; set; } = new();

    public string? CustomModel { get; set; }
    public string? CustomEmbeddingModel { get; set; }
    public string? CustomBaseUrl { get; set; }

    public bool DemoMode { get; set; } = false;
}

public class JobFinderResultViewModel
{
    public List<ScoringResult> RankedResults { get; set; } = new();
    public string CandidateSummary { get; set; } = string.Empty;
    public int ResumeChunkCount { get; set; }
    public int TotalJobsProcessed { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public bool OllamaConnected { get; set; } = true;
    public string ActiveModel { get; set; } = string.Empty;
    public string ActiveEmbeddingModel { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class OllamaStatusViewModel
{
    public bool IsConnected { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public List<string> AvailableModels { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
