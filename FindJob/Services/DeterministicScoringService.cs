using FindJob.Models;

namespace FindJob.Services;

public class DeterministicScoringService : IDeterministicScoringService
{
    private readonly ISkillDictionaryService _skillDictionary;

    public DeterministicScoringService(ISkillDictionaryService skillDictionary)
    {
        _skillDictionary = skillDictionary;
    }

    public ScoreBreakdown ComputeScore(
        ExtractedResumeProfile resume, 
        ExtractedJdProfile jd, 
        float semanticCosineSimilarity)
    {
        // 1. Skill Score (35% weight)
        var (matchedRequired, missingRequired) = _skillDictionary.CompareSkills(resume.Skills, jd.RequiredSkills);
        var (matchedNice, _) = _skillDictionary.CompareSkills(resume.Skills, jd.NiceToHaveSkills);

        double skillScoreVal;
        if (jd.RequiredSkills.Count > 0)
        {
            double reqRatio = (double)matchedRequired.Count / jd.RequiredSkills.Count;
            double niceRatio = jd.NiceToHaveSkills.Count > 0 
                ? (double)matchedNice.Count / jd.NiceToHaveSkills.Count 
                : 1.0;

            skillScoreVal = (reqRatio * 85.0) + (niceRatio * 15.0);
        }
        else
        {
            // If no explicit required skills were extracted, compare candidate skills against general keywords
            skillScoreVal = matchedRequired.Count > 0 ? 80.0 : 65.0;
        }
        int skillScore = (int)Math.Clamp(Math.Round(skillScoreVal), 0, 100);

        // 2. Experience Score (20% weight)
        double expScoreVal;
        if (jd.MinYearsExperience <= 0)
        {
            expScoreVal = 100.0;
        }
        else if (resume.TotalYearsExperience >= jd.MinYearsExperience)
        {
            expScoreVal = 100.0;
        }
        else
        {
            // Prorate: e.g. 3 years out of 5 required -> 60% with baseline
            double ratio = resume.TotalYearsExperience / jd.MinYearsExperience;
            expScoreVal = Math.Max(20.0, ratio * 100.0);
        }
        int experienceScore = (int)Math.Clamp(Math.Round(expScoreVal), 0, 100);

        // 3. Title / Seniority Score (10% weight)
        int candSeniority = GetSeniorityRank(resume.CurrentTitle);
        int jdSeniority = GetSeniorityRank(string.IsNullOrWhiteSpace(jd.Seniority) ? jd.JobTitle : jd.Seniority);
        int seniorityDiff = Math.Abs(candSeniority - jdSeniority);

        int titleScore = seniorityDiff switch
        {
            0 => 100,
            1 => 85,
            2 => 65,
            _ => 45
        };

        // 4. Education Score (10% weight)
        int candEdu = GetEducationRank(resume.Degree);
        int jdEdu = GetEducationRank(jd.RequiredDegree);

        int educationScore;
        if (jdEdu <= 1 || candEdu >= jdEdu)
        {
            educationScore = 100;
        }
        else
        {
            int eduDiff = jdEdu - candEdu;
            educationScore = eduDiff == 1 ? 80 : 60;
        }

        // 5. Semantic Vector Similarity Score (25% weight)
        // Normalize cosine (-1..1 to 0..100)
        int semanticScore = (int)Math.Clamp(Math.Round(Math.Max(0, semanticCosineSimilarity) * 100.0), 0, 100);
        if (semanticScore == 0 && semanticCosineSimilarity <= 0f)
        {
            // If vector check was offline, estimate from skill score & title
            semanticScore = (int)Math.Round((skillScore * 0.7) + (titleScore * 0.3));
        }

        // 6. Weighted Sum
        // Final = 0.35*Skill + 0.20*Exp + 0.10*Title + 0.10*Edu + 0.25*Semantic
        double weightedTotal = (0.35 * skillScore) +
                               (0.20 * experienceScore) +
                               (0.10 * titleScore) +
                               (0.10 * educationScore) +
                               (0.25 * semanticScore);

        int finalScore = (int)Math.Clamp(Math.Round(weightedTotal), 0, 100);

        // 7. Mandatory Skill Hard-Cap
        bool isCapped = false;
        string? capReason = null;

        if (jd.RequiredSkills.Count >= 2)
        {
            double reqMatchPercent = (double)matchedRequired.Count / jd.RequiredSkills.Count;
            if (reqMatchPercent < 0.40 && finalScore > 60)
            {
                finalScore = 60;
                isCapped = true;
                capReason = $"Score capped at 60% due to critical gaps in required core skills ({string.Join(", ", missingRequired.Take(3))}).";
            }
        }

        return new ScoreBreakdown
        {
            SkillScore = skillScore,
            ExperienceScore = experienceScore,
            TitleScore = titleScore,
            EducationScore = educationScore,
            SemanticScore = semanticScore,
            FinalScore = finalScore,
            IsCapped = isCapped,
            CapReason = capReason
        };
    }

    private static int GetSeniorityRank(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 2; // Default to Mid
        var lower = text.ToLowerInvariant();

        if (lower.Contains("intern") || lower.Contains("trainee") || lower.Contains("fresh") || lower.Contains("entry") || lower.Contains("junior"))
            return 1;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("specialist"))
            return 3;
        if (lower.Contains("lead") || lower.Contains("principal") || lower.Contains("staff") || lower.Contains("tech lead"))
            return 4;
        if (lower.Contains("architect") || lower.Contains("manager") || lower.Contains("director") || lower.Contains("head") || lower.Contains("vp"))
            return 5;

        return 2; // Mid-level
    }

    private static int GetEducationRank(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 1;
        var lower = text.ToLowerInvariant();

        if (lower.Contains("phd") || lower.Contains("doctorate")) return 5;
        if (lower.Contains("master") || lower.Contains("msc") || lower.Contains("m.sc") || lower.Contains("mba") || lower.Contains("m.tech")) return 4;
        if (lower.Contains("bachelor") || lower.Contains("bsc") || lower.Contains("b.sc") || lower.Contains("btech") || lower.Contains("b.tech") || lower.Contains("undergraduate") || lower.Contains("degree")) return 3;
        if (lower.Contains("diploma") || lower.Contains("associate")) return 2;

        return 1; // High school or not specified
    }
}
