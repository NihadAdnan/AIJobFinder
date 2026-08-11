using FindJob.Models;

namespace FindJob.Services;

public interface IOllamaService
{
    Task<OllamaStatusViewModel> CheckHealthAsync(string? baseUrl = null, CancellationToken cancellationToken = default);
    
    Task<float[]?> GetEmbeddingAsync(string text, string? model = null, string? baseUrl = null, CancellationToken cancellationToken = default);
    
    Task<List<float[]?>> GetEmbeddingsBatchAsync(List<string> texts, string? model = null, string? baseUrl = null, CancellationToken cancellationToken = default);
    
    float ComputeCosineSimilarity(float[]? vecA, float[]? vecB);
    
    Task<ExtractedResumeProfile> ExtractResumeProfileAsync(
        ResumeData resume, 
        string? model = null, 
        string? baseUrl = null, 
        CancellationToken cancellationToken = default);

    Task<ExtractedJdProfile> ExtractJdProfileAsync(
        JobData job, 
        string? model = null, 
        string? baseUrl = null, 
        CancellationToken cancellationToken = default);

    Task<string> GenerateMatchRationaleAsync(
        ExtractedResumeProfile resume, 
        ExtractedJdProfile jd, 
        ScoreBreakdown breakdown,
        List<string> matchedSkills,
        List<string> missingSkills,
        string? model = null, 
        string? baseUrl = null, 
        CancellationToken cancellationToken = default);
}
