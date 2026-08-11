using FindJob.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FindJob.Tests;

public class OllamaServiceTests
{
    private readonly OllamaService _service;

    public OllamaServiceTests()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            {"Ollama:BaseUrl", "http://localhost:11434"},
            {"Ollama:ChatModel", "llama3.1:8b"},
            {"Ollama:EmbeddingModel", "nomic-embed-text"}
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        var httpClientFactory = new TestHttpClientFactory();

        _service = new OllamaService(httpClientFactory, configuration, NullLogger<OllamaService>.Instance);
    }

    [Fact]
    public void ComputeCosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        float[] v1 = [1.0f, 2.0f, 3.0f];
        float[] v2 = [1.0f, 2.0f, 3.0f];

        var sim = _service.ComputeCosineSimilarity(v1, v2);

        Assert.True(Math.Abs(sim - 1.0f) < 0.001f);
    }

    [Fact]
    public void ComputeCosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        float[] v1 = [1.0f, 0.0f, 0.0f];
        float[] v2 = [0.0f, 1.0f, 0.0f];

        var sim = _service.ComputeCosineSimilarity(v1, v2);

        Assert.True(Math.Abs(sim - 0.0f) < 0.001f);
    }

    [Fact]
    public void ComputeCosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        float[] v1 = [1.0f, 0.0f, 0.0f];
        float[] v2 = [-1.0f, 0.0f, 0.0f];

        var sim = _service.ComputeCosineSimilarity(v1, v2);

        Assert.True(Math.Abs(sim - (-1.0f)) < 0.001f);
    }

    [Fact]
    public void ComputeCosineSimilarity_NullOrEmpty_ReturnsZero()
    {
        Assert.Equal(0f, _service.ComputeCosineSimilarity(null, [1.0f, 2.0f]));
        Assert.Equal(0f, _service.ComputeCosineSimilarity([1.0f, 2.0f], null));
        Assert.Equal(0f, _service.ComputeCosineSimilarity([], []));
        Assert.Equal(0f, _service.ComputeCosineSimilarity([1.0f], [1.0f, 2.0f]));
    }
}
