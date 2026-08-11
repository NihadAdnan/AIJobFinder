using FindJob.Services;
using Xunit;

namespace FindJob.Tests;

public class ResumeParserServiceTests
{
    private readonly ResumeParserService _service = new();

    [Fact]
    public void ParseText_WithStructuredSections_ExtractsChunksAndCandidate()
    {
        var sampleResume = @"JOHN DOE
Senior Backend Engineer | Dhaka
john.doe@example.com

SUMMARY
Experienced software engineer with 6 years building microservices with C# and ASP.NET Core.

TECHNICAL SKILLS
C#, ASP.NET Core, EF Core, SQL Server, Redis, Docker, Azure

WORK EXPERIENCE
Lead Engineer at Acme Corp (2021-Present)
- Built high throughput REST APIs.
- Designed database schemas and event messaging.

EDUCATION
B.Sc. in Computer Science, 2018";

        var result = _service.ParseText(sampleResume);

        Assert.True(result.IsSuccess);
        Assert.Equal("JOHN DOE", result.CandidateName);
        Assert.NotEmpty(result.Chunks);
        Assert.Contains(result.Chunks, c => c.Section.Contains("SKILL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Chunks, c => c.Section.Contains("EXPERIENCE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseText_EmptyString_ReturnsUnsuccessful()
    {
        var result = _service.ParseText("   ");
        Assert.False(result.IsSuccess);
    }
}
