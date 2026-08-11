using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace FindJob.Models;

public class ExtractedResumeProfile
{
    public string CandidateName { get; set; } = string.Empty;
    public string CurrentTitle { get; set; } = string.Empty;
    public double TotalYearsExperience { get; set; }
    public string Degree { get; set; } = string.Empty;
    public List<string> Skills { get; set; } = new();
    public List<string> Highlights { get; set; } = new();
}

public class ExtractedJdProfile
{
    public string JobTitle { get; set; } = string.Empty;
    public string Seniority { get; set; } = string.Empty; // Junior, Mid, Senior, Lead, Architect
    public double MinYearsExperience { get; set; }
    public string RequiredDegree { get; set; } = string.Empty; // None, Diploma, Bachelor, Master, PhD
    public List<string> RequiredSkills { get; set; } = new();
    public List<string> NiceToHaveSkills { get; set; } = new();
    public string CoreSummary { get; set; } = string.Empty;
}

public class ScoreBreakdown
{
    public int SkillScore { get; set; }       // 35% weight
    public int ExperienceScore { get; set; }  // 20% weight
    public int TitleScore { get; set; }       // 10% weight
    public int EducationScore { get; set; }   // 10% weight
    public int SemanticScore { get; set; }    // 25% weight
    public int FinalScore { get; set; }       // 0 - 100
    public bool IsCapped { get; set; }        // Hard-cap applied
    public string? CapReason { get; set; }
}

public class ScoringResult
{
    public string JobId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string SourceDomain { get; set; } = "Web";
    public int Score { get; set; } // 0 - 100
    
    public ScoreBreakdown Breakdown { get; set; } = new();

    public List<string> MatchedSkills { get; set; } = new();
    public List<string> MissingRequiredSkills { get; set; } = new();
    public List<string> NiceToHaveMatched { get; set; } = new();
    public string ExperienceGapText { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty; // 2-sentence rationale

    public float TopCosineSimilarity { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? Location { get; set; }
    public string? Salary { get; set; }
    public string? Experience { get; set; }
}

public class JobFinderRequestViewModel
{
    public IFormFile? ResumeFile { get; set; }
    public List<string> JobUrls { get; set; } = new();
    public List<string> ManualJdTexts { get; set; } = new();

    // User-confirmed / edited profile overrides from popup
    public string? OverriddenCandidateName { get; set; }
    public string? OverriddenTitle { get; set; }
    public double? OverriddenYearsExperience { get; set; }
    public string? OverriddenDegree { get; set; }
    public string? OverriddenSkills { get; set; } // Comma-separated or JSON list

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
