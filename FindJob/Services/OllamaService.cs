using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FindJob.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FindJob.Services;

public class OllamaService : IOllamaService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaService> _logger;
    private readonly string _defaultBaseUrl;
    private readonly string _defaultChatModel;
    private readonly string _defaultEmbeddingModel;

    public OllamaService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OllamaService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _defaultBaseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _defaultChatModel = configuration["Ollama:ChatModel"] ?? "llama3.1:8b";
        _defaultEmbeddingModel = configuration["Ollama:EmbeddingModel"] ?? "nomic-embed-text";
    }

    public async Task<OllamaStatusViewModel> CheckHealthAsync(string? baseUrl = null, CancellationToken cancellationToken = default)
    {
        var targetUrl = (baseUrl ?? _defaultBaseUrl).TrimEnd('/');
        var client = _httpClientFactory.CreateClient("OllamaHealthClient");

        try
        {
            var response = await client.GetAsync($"{targetUrl}/api/tags", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = JsonDocument.Parse(content);
                var models = new List<string>();

                if (doc.RootElement.TryGetProperty("models", out var modelsArray))
                {
                    foreach (var m in modelsArray.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var nameProp))
                        {
                            models.Add(nameProp.GetString() ?? "");
                        }
                    }
                }

                return new OllamaStatusViewModel
                {
                    IsConnected = true,
                    BaseUrl = targetUrl,
                    AvailableModels = models
                };
            }
        }
        catch { }

        return new OllamaStatusViewModel
        {
            IsConnected = false,
            BaseUrl = targetUrl,
            ErrorMessage = "Local Ollama server is offline or unreachable."
        };
    }

    public async Task<float[]?> GetEmbeddingAsync(string text, string? model = null, string? baseUrl = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var targetUrl = (baseUrl ?? _defaultBaseUrl).TrimEnd('/');
        var targetModel = model ?? _defaultEmbeddingModel;
        var client = _httpClientFactory.CreateClient("OllamaClient");

        var payload = new
        {
            model = targetModel,
            prompt = text.Length > 2000 ? text[..2000] : text
        };

        try
        {
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{targetUrl}/api/embeddings", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var respStr = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(respStr);
                if (doc.RootElement.TryGetProperty("embedding", out var embArray))
                {
                    var result = new float[embArray.GetArrayLength()];
                    int idx = 0;
                    foreach (var item in embArray.EnumerateArray())
                    {
                        result[idx++] = item.GetSingle();
                    }
                    return result;
                }
            }
        }
        catch { }

        return null;
    }

    public async Task<List<float[]?>> GetEmbeddingsBatchAsync(List<string> texts, string? model = null, string? baseUrl = null, CancellationToken cancellationToken = default)
    {
        var results = new List<float[]?>();
        foreach (var t in texts)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var emb = await GetEmbeddingAsync(t, model, baseUrl, cancellationToken);
            results.Add(emb);
        }
        return results;
    }

    public float ComputeCosineSimilarity(float[]? vecA, float[]? vecB)
    {
        if (vecA == null || vecB == null || vecA.Length == 0 || vecB.Length == 0 || vecA.Length != vecB.Length)
        {
            return 0f;
        }

        double dot = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < vecA.Length; i++)
        {
            dot += vecA[i] * vecB[i];
            normA += vecA[i] * vecA[i];
            normB += vecB[i] * vecB[i];
        }

        if (normA <= 0 || normB <= 0) return 0f;

        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }

    public async Task<ExtractedResumeProfile> ExtractResumeProfileAsync(
        ResumeData resume, 
        string? model = null, 
        string? baseUrl = null, 
        CancellationToken cancellationToken = default)
    {
        var targetUrl = (baseUrl ?? _defaultBaseUrl).TrimEnd('/');
        var targetModel = model ?? _defaultChatModel;

        // Pull top relevant sections (Summary, Skills, Experience, Education)
        var retrievedContext = string.Join("\n\n", resume.Chunks.Take(5).Select(c => $"[{c.Section}]\n{c.Text}"));
        if (string.IsNullOrWhiteSpace(retrievedContext)) retrievedContext = resume.RawText;

        var systemPrompt = "You are an expert AI resume parser. Extract the candidate's structured profile strictly as JSON.";
        var userPrompt = $@"Extract structured candidate facts from the resume text below.
Respond ONLY with a valid JSON object matching this schema:
{{
  ""candidateName"": string,
  ""currentTitle"": string,
  ""totalYearsExperience"": number,
  ""degree"": string,
  ""skills"": [string, string, ...],
  ""highlights"": [string, string, ...]
}}

RESUME TEXT:
{retrievedContext}";

        try
        {
            var client = _httpClientFactory.CreateClient("OllamaClient");
            var payload = new
            {
                model = targetModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                format = "json",
                stream = false,
                options = new { temperature = 0.1 }
            };

            var jsonBody = JsonSerializer.Serialize(payload);
            using var requestContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{targetUrl}/api/chat", requestContent, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var respStr = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(respStr);
                if (doc.RootElement.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var contentProp))
                {
                    var content = contentProp.GetString() ?? "{}";
                    var parsed = JsonSerializer.Deserialize<ExtractedResumeProfile>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (parsed != null && parsed.Skills.Count > 0)
                    {
                        return parsed;
                    }
                }
            }
        }
        catch { }

        // Fallback: High-quality heuristic extractor
        return HeuristicExtractResume(resume);
    }

    public async Task<ExtractedJdProfile> ExtractJdProfileAsync(
        JobData job, 
        string? model = null, 
        string? baseUrl = null, 
        CancellationToken cancellationToken = default)
    {
        var targetUrl = (baseUrl ?? _defaultBaseUrl).TrimEnd('/');
        var targetModel = model ?? _defaultChatModel;

        var retrievedContext = job.ToFormattedContext();

        var systemPrompt = "You are an expert technical recruiter. Extract structured job requirements strictly as JSON.";
        var userPrompt = $@"Extract structured job requirements from the job posting below.
Respond ONLY with a valid JSON object matching this schema:
{{
  ""jobTitle"": string,
  ""seniority"": string,
  ""minYearsExperience"": number,
  ""requiredDegree"": string,
  ""requiredSkills"": [string, string, ...],
  ""niceToHaveSkills"": [string, string, ...],
  ""coreSummary"": string
}}

JOB POSTING:
{retrievedContext}";

        try
        {
            var client = _httpClientFactory.CreateClient("OllamaClient");
            var payload = new
            {
                model = targetModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                format = "json",
                stream = false,
                options = new { temperature = 0.1 }
            };

            var jsonBody = JsonSerializer.Serialize(payload);
            using var requestContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{targetUrl}/api/chat", requestContent, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var respStr = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(respStr);
                if (doc.RootElement.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var contentProp))
                {
                    var content = contentProp.GetString() ?? "{}";
                    var parsed = JsonSerializer.Deserialize<ExtractedJdProfile>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (parsed != null && parsed.RequiredSkills.Count > 0)
                    {
                        return parsed;
                    }
                }
            }
        }
        catch { }

        // Fallback: High-quality heuristic extractor
        return HeuristicExtractJd(job);
    }

    public async Task<string> GenerateMatchRationaleAsync(
        ExtractedResumeProfile resume, 
        ExtractedJdProfile jd, 
        ScoreBreakdown breakdown,
        List<string> matchedSkills,
        List<string> missingSkills,
        string? model = null, 
        string? baseUrl = null, 
        CancellationToken cancellationToken = default)
    {
        var targetUrl = (baseUrl ?? _defaultBaseUrl).TrimEnd('/');
        var targetModel = model ?? _defaultChatModel;

        var systemPrompt = "You are a senior technical hiring manager. Write exactly 2 concise, professional sentences explaining the candidate match score.";
        var userPrompt = $@"Candidate: {resume.CandidateName} ({resume.CurrentTitle}, {resume.TotalYearsExperience} yrs exp).
Target Role: {jd.JobTitle} ({jd.Seniority}, requires {jd.MinYearsExperience} yrs exp).
Match Score: {breakdown.FinalScore}% (Skills: {breakdown.SkillScore}%, Experience: {breakdown.ExperienceScore}%, Title: {breakdown.TitleScore}%, Degree: {breakdown.EducationScore}%, Semantic: {breakdown.SemanticScore}%).
Matched Key Skills: {string.Join(", ", matchedSkills.Take(5))}.
Missing Key Skills: {(missingSkills.Count > 0 ? string.Join(", ", missingSkills.Take(3)) : "None")}.
{(breakdown.IsCapped ? $"Note: {breakdown.CapReason}" : "")}

Write exactly 2 clear, professional sentences explaining why the candidate received this match score, highlighting their key strengths and any critical gaps.";

        try
        {
            var client = _httpClientFactory.CreateClient("OllamaClient");
            var payload = new
            {
                model = targetModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                stream = false,
                options = new { temperature = 0.3 }
            };

            var jsonBody = JsonSerializer.Serialize(payload);
            using var requestContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{targetUrl}/api/chat", requestContent, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var respStr = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(respStr);
                if (doc.RootElement.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var contentProp))
                {
                    var text = contentProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length > 20)
                    {
                        return text;
                    }
                }
            }
        }
        catch { }

        // Fallback: Clean heuristic rationale
        return GenerateHeuristicRationale(resume, jd, breakdown, matchedSkills, missingSkills);
    }

    private static ExtractedResumeProfile HeuristicExtractResume(ResumeData resume)
    {
        var raw = resume.RawText;
        var skills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Core catalog
        var knownSkills = new[]
        {
            "C#", ".NET", "ASP.NET Core", "Entity Framework", "SQL Server", "PostgreSQL", "MySQL", "MongoDB", "Redis",
            "JavaScript", "TypeScript", "React", "Angular", "Vue", "Next.js", "Node.js", "Express", "HTML5", "CSS3", "Tailwind",
            "Python", "Django", "FastAPI", "Java", "Spring Boot", "Go", "Golang", "C++",
            "Docker", "Kubernetes", "AWS", "Azure", "GCP", "CI/CD", "Git", "Linux",
            "REST", "GraphQL", "Microservices", "CQRS", "RabbitMQ", "Kafka",
            "Machine Learning", "LLM", "RAG", "Ollama", "Embeddings", "NLP"
        };

        foreach (var s in knownSkills)
        {
            if (Regex.IsMatch(raw, $@"\b{Regex.Escape(s)}\b", RegexOptions.IgnoreCase))
            {
                skills.Add(s);
            }
        }

        // Years of experience extraction
        double years = 0;
        var yrMatch = Regex.Match(raw, @"(\d+)\+?\s*(?:years?|yrs?)(?:\s+of)?\s+(?:experience|exp)", RegexOptions.IgnoreCase);
        if (yrMatch.Success && double.TryParse(yrMatch.Groups[1].Value, out var parsedYr))
        {
            years = parsedYr;
        }
        else if (raw.Contains("Senior", StringComparison.OrdinalIgnoreCase))
        {
            years = 5;
        }
        else
        {
            years = 3;
        }

        // Degree
        string degree = "Bachelor of Science";
        if (Regex.IsMatch(raw, @"\b(M\.?Sc|Master|MBA)\b", RegexOptions.IgnoreCase)) degree = "Master's Degree";
        else if (Regex.IsMatch(raw, @"\b(B\.?Sc|Bachelor|BTech|B\.Tech)\b", RegexOptions.IgnoreCase)) degree = "Bachelor's Degree";
        else if (Regex.IsMatch(raw, @"\b(Diploma|Associate)\b", RegexOptions.IgnoreCase)) degree = "Diploma";

        // Current title
        string title = "Software Engineer";
        var titleMatch = Regex.Match(raw, @"\b(Senior\s+Software\s+Engineer|Full\s+Stack\s+Developer|Software\s+Engineer|Backend\s+Developer|Frontend\s+Developer|Tech\s+Lead|DevOps\s+Engineer)\b", RegexOptions.IgnoreCase);
        if (titleMatch.Success) title = titleMatch.Groups[1].Value;

        return new ExtractedResumeProfile
        {
            CandidateName = resume.CandidateName ?? "Candidate",
            CurrentTitle = title,
            TotalYearsExperience = years,
            Degree = degree,
            Skills = skills.ToList()
        };
    }

    private static ExtractedJdProfile HeuristicExtractJd(JobData job)
    {
        var text = job.ToFormattedContext();
        var reqSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var niceSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var knownSkills = new[]
        {
            "C#", ".NET", "ASP.NET Core", "Entity Framework", "SQL Server", "PostgreSQL", "MySQL", "MongoDB", "Redis",
            "JavaScript", "TypeScript", "React", "Angular", "Vue", "Next.js", "Node.js", "Express", "HTML5", "CSS3", "Tailwind",
            "Python", "Django", "FastAPI", "Java", "Spring Boot", "Go", "Golang", "C++",
            "Docker", "Kubernetes", "AWS", "Azure", "GCP", "CI/CD", "Git", "Linux",
            "REST", "GraphQL", "Microservices", "CQRS", "RabbitMQ", "Kafka",
            "Machine Learning", "LLM", "RAG", "NLP"
        };

        foreach (var s in knownSkills)
        {
            if (Regex.IsMatch(job.Requirements, $@"\b{Regex.Escape(s)}\b", RegexOptions.IgnoreCase) || 
                Regex.IsMatch(text, $@"\b{Regex.Escape(s)}\b", RegexOptions.IgnoreCase))
            {
                reqSkills.Add(s);
            }
        }

        // Min years extraction
        double minYears = 0;
        var expSource = $"{job.Experience} {job.Requirements} {text}";
        var yrMatch = Regex.Match(expSource, @"(\d+)(?:\s*(?:to|-)\s*\d+)?\+?\s*(?:years?|yrs?)", RegexOptions.IgnoreCase);
        if (yrMatch.Success && double.TryParse(yrMatch.Groups[1].Value, out var parsedYr))
        {
            minYears = parsedYr;
        }

        // Seniority
        string seniority = "Mid-Level";
        if (Regex.IsMatch(job.Title, @"\b(Senior|Sr\.?|Lead|Principal)\b", RegexOptions.IgnoreCase)) seniority = "Senior";
        else if (Regex.IsMatch(job.Title, @"\b(Junior|Entry|Associate|Intern)\b", RegexOptions.IgnoreCase)) seniority = "Junior";

        // Degree
        string reqDegree = "Bachelor's Degree";
        if (Regex.IsMatch(job.Education, @"\b(Master|MSc|MBA)\b", RegexOptions.IgnoreCase)) reqDegree = "Master's Degree";
        else if (Regex.IsMatch(job.Education, @"\b(Diploma)\b", RegexOptions.IgnoreCase)) reqDegree = "Diploma";

        return new ExtractedJdProfile
        {
            JobTitle = job.Title,
            Seniority = seniority,
            MinYearsExperience = minYears,
            RequiredDegree = reqDegree,
            RequiredSkills = reqSkills.ToList(),
            NiceToHaveSkills = niceSkills.ToList(),
            CoreSummary = job.Responsibilities
        };
    }

    private static string GenerateHeuristicRationale(
        ExtractedResumeProfile resume, 
        ExtractedJdProfile jd, 
        ScoreBreakdown breakdown,
        List<string> matchedSkills,
        List<string> missingSkills)
    {
        var sb = new StringBuilder();

        if (breakdown.FinalScore >= 80)
        {
            sb.Append($"Strong technical alignment with {matchedSkills.Count} core qualifications matched, including {string.Join(", ", matchedSkills.Take(3))}. ");
            sb.Append($"The candidate's {resume.TotalYearsExperience} years of experience and {resume.CurrentTitle} background comfortably meet the requirements for this {jd.JobTitle} role.");
        }
        else if (breakdown.FinalScore >= 50)
        {
            sb.Append($"Solid foundational match with key qualifications in {string.Join(", ", matchedSkills.Take(3))}. ");
            if (missingSkills.Count > 0)
            {
                sb.Append($"However, targeted growth is recommended in required competencies: {string.Join(", ", missingSkills.Take(2))}.");
            }
            else
            {
                sb.Append($"Seniority and experience metrics align moderately well with the {jd.JobTitle} role expectations.");
            }
        }
        else
        {
            sb.Append($"Noticeable qualification gap for this position. ");
            if (missingSkills.Count > 0)
            {
                sb.Append($"Core prerequisites ({string.Join(", ", missingSkills.Take(3))}) and role-specific experience requirements show low overlap.");
            }
            else
            {
                sb.Append($"Significant difference observed between current candidate profile and required job responsibilities.");
            }
        }

        return sb.ToString();
    }
}
