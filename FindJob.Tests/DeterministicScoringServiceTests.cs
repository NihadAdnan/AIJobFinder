using FindJob.Models;
using FindJob.Services;
using Xunit;

namespace FindJob.Tests;

public class DeterministicScoringServiceTests
{
    private readonly DeterministicScoringService _scoringService;

    public DeterministicScoringServiceTests()
    {
        _scoringService = new DeterministicScoringService(new SkillDictionaryService());
    }

    [Fact]
    public void ComputeScore_HighMatchProfile_ProducesHighFinalScore()
    {
        var resume = new ExtractedResumeProfile
        {
            CandidateName = "Rahim Ahmed",
            CurrentTitle = "Senior Software Engineer",
            TotalYearsExperience = 6.0,
            Degree = "Bachelor of Science in Computer Science",
            Skills = new List<string> { "C#", ".NET Core", "SQL Server", "Docker", "Angular", "Redis" }
        };

        var jd = new ExtractedJdProfile
        {
            JobTitle = "Senior .NET Developer",
            Seniority = "Senior",
            MinYearsExperience = 5.0,
            RequiredDegree = "Bachelor's Degree",
            RequiredSkills = new List<string> { "C#", ".NET Core", "SQL Server" },
            NiceToHaveSkills = new List<string> { "Docker", "Angular" }
        };

        var breakdown = _scoringService.ComputeScore(resume, jd, 0.88f);

        Assert.NotNull(breakdown);
        Assert.True(breakdown.FinalScore >= 80, $"Expected score >= 80, got {breakdown.FinalScore}");
        Assert.Equal(100, breakdown.SkillScore);
        Assert.Equal(100, breakdown.ExperienceScore);
        Assert.Equal(100, breakdown.TitleScore);
        Assert.Equal(100, breakdown.EducationScore);
        Assert.False(breakdown.IsCapped);
    }

    [Fact]
    public void ComputeScore_MissingMandatorySkills_AppliesHardCapAt60Percent()
    {
        var resume = new ExtractedResumeProfile
        {
            CandidateName = "Frontend Dev",
            CurrentTitle = "Senior Frontend Engineer",
            TotalYearsExperience = 8.0,
            Degree = "Master in Computer Science",
            Skills = new List<string> { "React", "HTML5", "CSS3", "JavaScript" }
        };

        var jd = new ExtractedJdProfile
        {
            JobTitle = "Lead Rust & Systems Architect",
            Seniority = "Lead",
            MinYearsExperience = 5.0,
            RequiredDegree = "Bachelor",
            RequiredSkills = new List<string> { "Rust", "C++", "Linux Kernel", "LLVM", "Distributed Systems" },
            NiceToHaveSkills = new List<string>()
        };

        // High semantic cosine to test if hard-cap limits it
        var breakdown = _scoringService.ComputeScore(resume, jd, 0.95f);

        Assert.NotNull(breakdown);
        Assert.True(breakdown.IsCapped);
        Assert.Equal(60, breakdown.FinalScore);
        Assert.NotNull(breakdown.CapReason);
    }
}
