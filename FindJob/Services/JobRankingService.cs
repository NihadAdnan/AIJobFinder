using System.Diagnostics;
using FindJob.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FindJob.Services;

public class JobRankingService : IJobRankingService
{
    private readonly IResumeParserService _resumeParserService;
    private readonly IBdjobsService _bdjobsService;
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<JobRankingService> _logger;
    private readonly string _defaultChatModel;
    private readonly string _defaultEmbeddingModel;
    private readonly string _defaultBaseUrl;

    public JobRankingService(
        IResumeParserService resumeParserService,
        IBdjobsService bdjobsService,
        IOllamaService ollamaService,
        IConfiguration configuration,
        ILogger<JobRankingService> logger)
    {
        _resumeParserService = resumeParserService;
        _bdjobsService = bdjobsService;
        _ollamaService = ollamaService;
        _logger = logger;
        _defaultChatModel = configuration["Ollama:ChatModel"] ?? "llama3.1:8b";
        _defaultEmbeddingModel = configuration["Ollama:EmbeddingModel"] ?? "nomic-embed-text";
        _defaultBaseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
    }

    public async Task<JobFinderResultViewModel> ProcessAndRankJobsAsync(JobFinderRequestViewModel request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var resultViewModel = new JobFinderResultViewModel
        {
            ActiveModel = !string.IsNullOrWhiteSpace(request.CustomModel) ? request.CustomModel : _defaultChatModel,
            ActiveEmbeddingModel = !string.IsNullOrWhiteSpace(request.CustomEmbeddingModel) ? request.CustomEmbeddingModel : _defaultEmbeddingModel
        };

        var targetBaseUrl = !string.IsNullOrWhiteSpace(request.CustomBaseUrl) ? request.CustomBaseUrl : _defaultBaseUrl;

        // 1. Check Ollama Health
        var health = await _ollamaService.CheckHealthAsync(targetBaseUrl, cancellationToken);
        resultViewModel.OllamaConnected = health.IsConnected;
        if (!health.IsConnected)
        {
            resultViewModel.ActiveModel = "AI Match Engine (Smart Heuristics)";
            resultViewModel.ActiveEmbeddingModel = "In-Memory Keyword Matcher";
        }

        // 2. Parse Resume
        ResumeData resumeData;
        if (request.DemoMode || (request.ResumeFile == null && request.JobUrls.Count > 0))
        {
            resumeData = GetSampleResumeData();
        }
        else if (request.ResumeFile != null)
        {
            resumeData = await _resumeParserService.ParseResumeAsync(request.ResumeFile, cancellationToken);
        }
        else
        {
            resultViewModel.ErrorMessage = "Please upload a valid resume (PDF or DOCX).";
            stopwatch.Stop();
            resultViewModel.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            return resultViewModel;
        }

        if (!resumeData.IsSuccess)
        {
            resultViewModel.ErrorMessage = resumeData.ErrorMessage ?? "Failed to parse resume content.";
            stopwatch.Stop();
            resultViewModel.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            return resultViewModel;
        }

        resultViewModel.ResumeChunkCount = resumeData.Chunks.Count;
        resultViewModel.CandidateSummary = GenerateCandidateSummary(resumeData);

        // 3. Extract & Fetch Bdjobs data (max 5 URLs)
        var validUrls = request.JobUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Take(5).ToList();
        if (validUrls.Count == 0 && request.DemoMode)
        {
            validUrls = GetSampleJobUrls();
        }

        if (validUrls.Count == 0)
        {
            resultViewModel.ErrorMessage = "Please provide at least one valid Bdjobs posting URL.";
            stopwatch.Stop();
            resultViewModel.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            return resultViewModel;
        }

        var fetchedJobs = await _bdjobsService.FetchMultipleJobsAsync(validUrls, cancellationToken);
        resultViewModel.TotalJobsProcessed = fetchedJobs.Count;

        // 4. In-Memory Embeddings (RAG Pipeline)
        if (resultViewModel.OllamaConnected)
        {
            try
            {
                // Embed resume chunks in-memory
                for (int i = 0; i < resumeData.Chunks.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var chunk = resumeData.Chunks[i];
                    chunk.Vector = await _ollamaService.GetEmbeddingAsync(chunk.Text, resultViewModel.ActiveEmbeddingModel, targetBaseUrl, cancellationToken);
                }

                // Embed job requirement texts in-memory
                foreach (var job in fetchedJobs.Where(j => j.IsSuccess))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var jobSummaryForEmbedding = $"{job.Title} {job.Requirements} {job.Responsibilities}";
                    if (!string.IsNullOrWhiteSpace(jobSummaryForEmbedding))
                    {
                        var jobVec = await _ollamaService.GetEmbeddingAsync(jobSummaryForEmbedding, resultViewModel.ActiveEmbeddingModel, targetBaseUrl, cancellationToken);
                        if (jobVec != null && job.Chunks.Count > 0)
                        {
                            job.Chunks[0].Vector = jobVec;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Embedding step encountered an issue, proceeding with direct section retrieval");
            }
        }

        // 5. Score Each Job Independently (bounded concurrency = 2 to protect local hardware)
        var scoringResults = new List<ScoringResult>();
        using var scoringSemaphore = new SemaphoreSlim(2, 2);

        var scoringTasks = fetchedJobs.Select(async job =>
        {
            if (!job.IsSuccess)
            {
                return new ScoringResult
                {
                    JobId = job.JobId,
                    Title = "Unavailable Posting",
                    Company = "Bdjobs",
                    SourceUrl = job.SourceUrl,
                    Score = 0,
                    IsSuccess = false,
                    ErrorMessage = job.ErrorMessage ?? "Job listing could not be retrieved from Bdjobs."
                };
            }

            await scoringSemaphore.WaitAsync(cancellationToken);
            try
            {
                // In-Memory Cosine Similarity Matcher: Pick Top-3 relevant resume chunks
                List<TextChunk> topRelevantChunks;
                float topSimilarity = 0f;

                var jobVector = job.Chunks.FirstOrDefault(c => c.Vector != null)?.Vector;
                var chunksWithVectors = resumeData.Chunks.Where(c => c.Vector != null).ToList();

                if (jobVector != null && chunksWithVectors.Count > 0)
                {
                    var similarityRanked = chunksWithVectors
                        .Select(c => new { Chunk = c, Sim = _ollamaService.ComputeCosineSimilarity(jobVector, c.Vector) })
                        .OrderByDescending(x => x.Sim)
                        .ToList();

                    topSimilarity = similarityRanked.FirstOrDefault()?.Sim ?? 0f;
                    topRelevantChunks = similarityRanked.Take(3).Select(x => x.Chunk).ToList();
                }
                else
                {
                    // Fallback to Skills, Experience and Summary sections
                    topRelevantChunks = resumeData.Chunks
                        .Where(c => c.Section.Contains("SKILL", StringComparison.OrdinalIgnoreCase) || 
                                    c.Section.Contains("EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
                                    c.Section.Contains("PROJECT", StringComparison.OrdinalIgnoreCase) ||
                                    c.Section.Contains("SUMMARY", StringComparison.OrdinalIgnoreCase))
                        .Take(3)
                        .ToList();

                    if (topRelevantChunks.Count == 0)
                    {
                        topRelevantChunks = resumeData.Chunks.Take(3).ToList();
                    }
                }

                // Prompt LLM for structured scoring
                return await _ollamaService.ScoreJobMatchAsync(
                    job, 
                    resumeData, 
                    topRelevantChunks, 
                    topSimilarity, 
                    resultViewModel.ActiveModel, 
                    targetBaseUrl, 
                    cancellationToken);
            }
            finally
            {
                scoringSemaphore.Release();
            }
        });

        var completedScores = await Task.WhenAll(scoringTasks);

        // 6. Server-Side Ranking: Sort highest to lowest score
        resultViewModel.RankedResults = completedScores
            .OrderByDescending(r => r.IsSuccess)
            .ThenByDescending(r => r.Score)
            .ToList();

        stopwatch.Stop();
        resultViewModel.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        return resultViewModel;
    }

    private static string GenerateCandidateSummary(ResumeData resume)
    {
        if (!string.IsNullOrWhiteSpace(resume.CandidateName))
        {
            return $"Candidate: {resume.CandidateName} ({resume.Chunks.Count} extracted section blocks)";
        }
        return $"Resume successfully parsed into {resume.Chunks.Count} section chunks.";
    }

    private static ResumeData GetSampleResumeData()
    {
        var sampleText = @"RAHIM AHMED
Full Stack .NET & AI Software Engineer | Dhaka, Bangladesh
Email: rahim.dev@example.com | GitHub: github.com/rahimdev | LinkedIn: linkedin.com/in/rahimdev

PROFESSIONAL SUMMARY
Results-driven Senior Full Stack Software Engineer with 5+ years of hands-on experience designing and developing enterprise web applications, microservices, and AI-driven solutions using ASP.NET Core, C#, Entity Framework Core, SQL Server, Angular, and React. Passionate about LLMs, RAG pipelines, and local AI orchestration with Ollama.

TECHNICAL SKILLS
- Languages: C#, JavaScript, TypeScript, SQL, Python
- Backend: ASP.NET Core 8/10, Web API, Minimal APIs, EF Core, Dapper, MediatR, SignalR, RabbitMQ
- Frontend: Angular 16+, React, Bootstrap 5, Tailwind CSS, HTML5, CSS3/SCSS
- Databases: MS SQL Server, PostgreSQL, Redis, MongoDB
- Cloud & DevOps: Docker, Kubernetes, Azure App Services, GitHub Actions, CI/CD
- AI & LLM: Ollama, Semantic Kernel, LangChain, Embeddings, Vector Search, Cosine Similarity
- Architecture: Clean Architecture, CQRS, Domain-Driven Design (DDD), Microservices, RESTful APIs

WORK EXPERIENCE
Senior Software Engineer | TechSolutions Ltd. | Dhaka, Bangladesh
June 2022 - Present
- Architected and deployed high-performance ASP.NET Core microservices handling over 500,000 daily API requests.
- Integrated AI capabilities using local Ollama LLMs and vector embeddings for internal search and resume screening.
- Improved database query performance by 40% using Redis caching and optimized EF Core queries.
- Mentored junior engineers and led Agile sprint planning sessions.

Software Engineer | BrainStation 23 | Dhaka, Bangladesh
January 2020 - May 2022
- Developed scalable fintech web portals using C#, ASP.NET Core MVC, and Angular.
- Built secure RESTful APIs with JWT authentication, role-based authorization, and rate limiting.
- Automated deployment workflows using Docker containers and Azure DevOps CI/CD pipelines.

EDUCATION
Bachelor of Science in Computer Science & Engineering (B.Sc. in CSE)
Bangladesh University of Engineering and Technology (BUET) | 2015 - 2019
CGPA: 3.82 / 4.00

PROJECTS
- AI Job Matcher: Built an in-memory RAG system evaluating job descriptions against candidate profiles using ASP.NET Core and Ollama.
- Enterprise E-Commerce Platform: Scalable microservices ecosystem with RabbitMQ, Redis, and PostgreSQL.";

        var lines = sampleText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chunks = new List<TextChunk>
        {
            new("RAHIM AHMED - Senior Full Stack Software Engineer with 5+ years experience in ASP.NET Core, C#, SQL Server, Angular, and React.", "SUMMARY"),
            new("Technical Skills: C#, ASP.NET Core 8/10, EF Core, SQL Server, PostgreSQL, Redis, Docker, Kubernetes, Angular, React, Ollama, Vector Search.", "SKILLS"),
            new("Senior Software Engineer at TechSolutions Ltd (2022-Present): Architected high-performance ASP.NET Core microservices, integrated local Ollama LLM RAG pipelines.", "EXPERIENCE"),
            new("Software Engineer at BrainStation 23 (2020-2022): Developed fintech web portals with C#, ASP.NET Core MVC, and Angular.", "EXPERIENCE"),
            new("Education: B.Sc. in CSE from BUET (2015-2019), CGPA: 3.82/4.00.", "EDUCATION")
        };

        return new ResumeData
        {
            RawText = sampleText,
            CandidateName = "Rahim Ahmed",
            Chunks = chunks,
            IsSuccess = true
        };
    }

    private static List<string> GetSampleJobUrls()
    {
        return new List<string>
        {
            "https://jobs.bdjobs.com/jobdetails.asp?id=1358920",
            "https://jobs.bdjobs.com/jobdetails.asp?id=1359401",
            "https://jobs.bdjobs.com/jobdetails.asp?id=1357642"
        };
    }
}
