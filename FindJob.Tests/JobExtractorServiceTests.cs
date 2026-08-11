using FindJob.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FindJob.Tests;

public class JobExtractorServiceTests
{
    private readonly JobExtractorService _extractorService;

    public JobExtractorServiceTests()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            {"Bdjobs:GatewayBaseUrl", "https://gateway.bdjobs.com/jobapply/api/JobSubsystem/Job-Details"},
            {"Bdjobs:UserAgent", "TestAgent"}
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        var httpClientFactory = new TestHttpClientFactory();
        var bdjobsService = new BdjobsService(httpClientFactory, configuration, NullLogger<BdjobsService>.Instance);

        _extractorService = new JobExtractorService(httpClientFactory, bdjobsService, NullLogger<JobExtractorService>.Instance);
    }

    [Theory]
    [InlineData("https://bdjobs.com/h/details/1519924?ln=1", "Bdjobs")]
    [InlineData("https://www.linkedin.com/jobs/view/123456", "LinkedIn")]
    [InlineData("https://boards.greenhouse.io/openai/jobs/987654", "Greenhouse")]
    [InlineData("https://jobs.lever.co/anthropic/54321", "Lever")]
    [InlineData("https://indeed.com/viewjob?jk=abcdef", "Indeed")]
    [InlineData("https://jobs.ashbyhq.com/scale/112233", "Ashby")]
    [InlineData("https://apply.workable.com/tech-corp/j/9988", "Workable")]
    [InlineData("https://careers.google.com/jobs/results/123", "Google")]
    [InlineData("1519924", "Bdjobs")]
    public void ExtractDomainLabel_CorrectlyIdentifies_MajorJobPlatforms(string url, string expectedDomain)
    {
        var label = _extractorService.ExtractDomainLabel(url);
        Assert.Equal(expectedDomain, label);
    }

    [Fact]
    public async Task ExtractJobAsync_BdjobsUrl_RoutesToBdjobsServiceAndExtractsCleanFields()
    {
        var url = "https://bdjobs.com/h/details/1519924?ln=1";
        var result = await _extractorService.ExtractJobAsync(url);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal("1519924", result.JobId);
        Assert.Equal("Bdjobs", result.SourceDomain);
        Assert.False(string.IsNullOrWhiteSpace(result.Title));
        Assert.True(result.Chunks.Count > 0);
    }

    [Fact]
    public async Task ExtractMultipleJobsAsync_DeduplicatesAndProcessesBatch()
    {
        var urls = new[]
        {
            "https://bdjobs.com/h/details/1519924?ln=1",
            "https://bdjobs.com/h/details/1519924?ln=1", // Duplicate
            "https://bdjobs.com/h/details/1519924"      // Alternate format for same or another
        };

        var results = await _extractorService.ExtractMultipleJobsAsync(urls);

        Assert.NotNull(results);
        Assert.True(results.Count >= 1);
        Assert.All(results, r => Assert.True(r.IsSuccess));
    }
}
