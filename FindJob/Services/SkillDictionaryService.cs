using System.Text.RegularExpressions;

namespace FindJob.Services;

public class SkillDictionaryService : ISkillDictionaryService
{
    private static readonly Dictionary<string, string> SynonymMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Languages & Core
        { "js", "javascript" },
        { "ts", "typescript" },
        { "py", "python" },
        { "c#", "csharp" },
        { "c sharp", "csharp" },
        { "golang", "go" },
        { ".net", "dotnet" },
        { ".net core", "dotnet" },
        { "asp.net", "dotnet" },
        { "asp.net core", "dotnet" },
        { "c++", "cpp" },

        // Frontend
        { "react", "react" },
        { "reactjs", "react" },
        { "react.js", "react" },
        { "react native", "react native" },
        { "angular", "angular" },
        { "angularjs", "angular" },
        { "angular.js", "angular" },
        { "vue", "vue" },
        { "vuejs", "vue" },
        { "vue.js", "vue" },
        { "nextjs", "next.js" },
        { "next.js", "next.js" },
        { "tailwind", "tailwindcss" },
        { "tailwind css", "tailwindcss" },
        { "bootstrap 5", "bootstrap" },

        // Backend & Databases
        { "node", "nodejs" },
        { "node.js", "nodejs" },
        { "express", "expressjs" },
        { "express.js", "expressjs" },
        { "postgres", "postgresql" },
        { "pg", "postgresql" },
        { "mongo", "mongodb" },
        { "mssql", "sql server" },
        { "ms sql", "sql server" },
        { "sql server", "sql server" },
        { "ef core", "entity framework" },
        { "entity framework core", "entity framework" },

        // DevOps & Cloud
        { "k8s", "kubernetes" },
        { "kube", "kubernetes" },
        { "docker container", "docker" },
        { "amazon web services", "aws" },
        { "google cloud", "gcp" },
        { "google cloud platform", "gcp" },
        { "azure cloud", "azure" },
        { "microsoft azure", "azure" },
        { "ci/cd", "cicd" },
        { "ci-cd", "cicd" },
        { "continuous integration", "cicd" },

        // AI / ML / Data
        { "ml", "machine learning" },
        { "ai", "artificial intelligence" },
        { "dl", "deep learning" },
        { "nlp", "natural language processing" },
        { "llm", "large language models" },
        { "llms", "large language models" },
        { "rag", "retrieval augmented generation" },
        { "genai", "generative ai" }
    };

    public string Normalize(string skill)
    {
        if (string.IsNullOrWhiteSpace(skill)) return string.Empty;

        var cleaned = skill.Trim().ToLowerInvariant();
        cleaned = Regex.Replace(cleaned, @"[^\w\s\+\#\.\/\-]", "");

        if (SynonymMap.TryGetValue(cleaned, out var canonical))
        {
            return canonical;
        }

        return cleaned;
    }

    public (List<string> Matched, List<string> Missing) CompareSkills(IEnumerable<string> candidateSkills, IEnumerable<string> targetSkills)
    {
        var matched = new List<string>();
        var missing = new List<string>();

        var normalizedCandidateList = candidateSkills
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => new { Original = s, Normalized = Normalize(s) })
            .ToList();

        foreach (var target in targetSkills.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var normTarget = Normalize(target);
            bool isFound = false;

            foreach (var cand in normalizedCandidateList)
            {
                if (IsSkillMatchInternal(cand.Normalized, normTarget) || IsSkillMatchInternal(cand.Original, target))
                {
                    matched.Add(target.Trim());
                    isFound = true;
                    break;
                }
            }

            if (!isFound)
            {
                missing.Add(target.Trim());
            }
        }

        return (matched.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), 
                missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public bool IsSkillMatch(string candidateSkill, string targetSkill)
    {
        return IsSkillMatchInternal(Normalize(candidateSkill), Normalize(targetSkill));
    }

    private static bool IsSkillMatchInternal(string cNorm, string tNorm)
    {
        if (string.IsNullOrWhiteSpace(cNorm) || string.IsNullOrWhiteSpace(tNorm)) return false;
        if (string.Equals(cNorm, tNorm, StringComparison.OrdinalIgnoreCase)) return true;

        // Substring / Word inclusion check
        if (cNorm.Length > 2 && tNorm.Length > 2)
        {
            if (cNorm.Contains(tNorm, StringComparison.OrdinalIgnoreCase) || 
                tNorm.Contains(cNorm, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
