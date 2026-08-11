using FindJob.Models;

namespace FindJob.Services;

public interface IDeterministicScoringService
{
    ScoreBreakdown ComputeScore(
        ExtractedResumeProfile resume, 
        ExtractedJdProfile jd, 
        float semanticCosineSimilarity);
}
