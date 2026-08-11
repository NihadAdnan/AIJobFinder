using FindJob.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FindJob.Tests;

public class BdjobsServiceTests
{
    private readonly BdjobsService _service;

    public BdjobsServiceTests()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            {"Bdjobs:GatewayBaseUrl", "https://gateway.bdjobs.com/jobapply/api/JobSubsystem/Job-Details"},
            {"Bdjobs:UserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"}
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        var httpClientFactory = new TestHttpClientFactory();

        _service = new BdjobsService(httpClientFactory, configuration, NullLogger<BdjobsService>.Instance);
    }

    [Theory]
    [InlineData("https://bdjobs.com/h/details/1519924?ln=1", "1519924")]
    [InlineData("https://jobs.bdjobs.com/jobdetails.asp?id=1358920", "1358920")]
    [InlineData("https://jobs.bdjobs.com/jobdetails.asp?ln=1&id=1359401&fcatId=8", "1359401")]
    [InlineData("https://bdjobs.com/jobs/1357642", "1357642")]
    [InlineData("https://bdjobs.com/jobs/details/1357642", "1357642")]
    [InlineData("https://bdjobs.com/details/1519924", "1519924")]
    [InlineData("1358100", "1358100")]
    [InlineData("   1358100   ", "1358100")]
    [InlineData("https://jobs.bdjobs.com/jobdetails.asp?ID=999888", "999888")]
    public void ExtractJobId_WithVariousUrls_ReturnsCorrectId(string url, string expectedId)
    {
        var result = _service.ExtractJobId(url);
        Assert.Equal(expectedId, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://google.com")]
    [InlineData("invalid-url")]
    public void ExtractJobId_WithInvalidUrls_ReturnsNull(string invalidUrl)
    {
        var result = _service.ExtractJobId(invalidUrl);
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchJobDetailsAsync_WithLive1519924Url_ParsesStructuredJobFields()
    {
        var result = await _service.FetchJobDetailsAsync("https://bdjobs.com/h/details/1519924?ln=1");

        Assert.NotNull(result);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("1519924", result.JobId);
        Assert.False(string.IsNullOrWhiteSpace(result.Title));
        Assert.False(string.IsNullOrWhiteSpace(result.Company));
        Assert.False(string.IsNullOrWhiteSpace(result.Requirements));
        Assert.NotEmpty(result.Chunks);
    }
}
