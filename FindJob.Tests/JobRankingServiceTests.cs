using FindJob.Models;
using FindJob.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FindJob.Tests;

public class JobRankingServiceTests
{
    private readonly JobRankingService _rankingService;

    public JobRankingServiceTests()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            {"Ollama:BaseUrl", "http://localhost:11434"},
            {"Ollama:ChatModel", "llama3.1:8b"},
            {"Ollama:EmbeddingModel", "nomic-embed-text"},
            {"Bdjobs:GatewayBaseUrl", "https://gateway.bdjobs.com/jobapply/api/JobSubsystem/Job-Details"},
            {"Bdjobs:UserAgent", "TestAgent"}
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        var httpClientFactory = new TestHttpClientFactory();

        var resumeParser = new ResumeParserService();
        var bdjobsService = new BdjobsService(httpClientFactory, configuration, NullLogger<BdjobsService>.Instance);
        var ollamaService = new OllamaService(httpClientFactory, configuration, NullLogger<OllamaService>.Instance);

        _rankingService = new JobRankingService(resumeParser, bdjobsService, ollamaService, configuration, NullLogger<JobRankingService>.Instance);
    }

    [Fact]
    public async Task ProcessAndRankJobsAsync_DemoMode_ExecutesSuccessfullyAndRanksDescending()
    {
        var request = new JobFinderRequestViewModel
        {
            DemoMode = true,
            JobUrls = new List<string>
            {
                "https://bdjobs.com/h/details/1519924?ln=1"
            }
        };

        var result = await _rankingService.ProcessAndRankJobsAsync(request);

        Assert.NotNull(result);
        Assert.True(result.RankedResults.Count > 0);
        Assert.True(result.ResumeChunkCount > 0);
        Assert.NotEmpty(result.CandidateSummary);

        // Verify descending sort order for successful jobs
        var successfulJobs = result.RankedResults.Where(r => r.IsSuccess).ToList();
        for (int i = 0; i < successfulJobs.Count - 1; i++)
        {
            Assert.True(successfulJobs[i].Score >= successfulJobs[i + 1].Score, 
                $"Scores should be sorted descending. Index {i} has score {successfulJobs[i].Score}, index {i+1} has score {successfulJobs[i+1].Score}");
        }

        // Verify that Reasoning does NOT expose internal exception strings or raw connection errors
        foreach (var job in successfulJobs)
        {
            Assert.False(string.IsNullOrWhiteSpace(job.Reasoning));
            Assert.DoesNotContain("No connection could be made", job.Reasoning, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("actively refused", job.Reasoning, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("localhost:11434", job.Reasoning, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LLM service unavailable", job.Reasoning, StringComparison.OrdinalIgnoreCase);
        }
    }
}
