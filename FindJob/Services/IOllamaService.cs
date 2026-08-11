using FindJob.Models;

namespace FindJob.Services;

public interface IOllamaService
{
    Task<OllamaStatusViewModel> CheckHealthAsync(string? baseUrl = null, CancellationToken cancellationToken = default);
    
    Task<float[]?> GetEmbeddingAsync(string text, string? model = null, string? baseUrl = null, CancellationToken cancellationToken = default);
    
    Task<List<float[]?>> GetEmbeddingsBatchAsync(List<string> texts, string? model = null, string? baseUrl = null, CancellationToken cancellationToken = default);
    
    float ComputeCosineSimilarity(float[]? vecA, float[]? vecB);
    
    Task<ScoringResult> ScoreJobMatchAsync(
        JobData job, 
        ResumeData resume, 
        List<TextChunk> relevantChunks, 
        float topSimilarityScore,
        string? chatModel = null, 
        string? baseUrl = null, 
        CancellationToken cancellationToken = default);
}
