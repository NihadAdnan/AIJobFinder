using FindJob.Services;
using Xunit;

namespace FindJob.Tests;

public class SkillDictionaryServiceTests
{
    private readonly SkillDictionaryService _skillDictionary = new();

    [Theory]
    [InlineData("js", "javascript")]
    [InlineData("JavaScript", "javascript")]
    [InlineData("react.js", "react")]
    [InlineData("reactjs", "react")]
    [InlineData("c#", "csharp")]
    [InlineData(".NET", "dotnet")]
    [InlineData("asp.net core", "dotnet")]
    [InlineData("k8s", "kubernetes")]
    [InlineData("Postgres", "postgresql")]
    [InlineData("AWS", "aws")]
    public void Normalize_CorrectlyStandardizes_CanonicalSynonyms(string input, string expected)
    {
        var result = _skillDictionary.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CompareSkills_MatchesCanonicalSynonymsAcrossLists()
    {
        var candidateSkills = new[] { "C#", "ASP.NET Core", "ReactJS", "PostgreSQL", "Docker", "k8s" };
        var requiredSkills = new[] { "csharp", ".net core", "react", "postgresql", "kubernetes", "python" };

        var (matched, missing) = _skillDictionary.CompareSkills(candidateSkills, requiredSkills);

        Assert.Contains("csharp", matched);
        Assert.Contains(".net core", matched);
        Assert.Contains("react", matched);
        Assert.Contains("postgresql", matched);
        Assert.Contains("kubernetes", matched);
        Assert.Contains("python", missing);
        Assert.Equal(5, matched.Count);
        Assert.Single(missing);
    }
}
