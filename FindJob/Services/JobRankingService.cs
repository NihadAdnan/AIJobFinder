using System.Diagnostics;
using FindJob.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FindJob.Services;

public class JobRankingService : IJobRankingService
{
    private readonly IResumeParserService _resumeParserService;
    private readonly IJobExtractorService _jobExtractorService;
    private readonly ISkillDictionaryService _skillDictionaryService;
    private readonly IDeterministicScoringService _deterministicScoringService;
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<JobRankingService> _logger;
    private readonly string _defaultChatModel;
    private readonly string _defaultEmbeddingModel;
    private readonly string _defaultBaseUrl;

    public JobRankingService(
        IResumeParserService resumeParserService,
        IJobExtractorService jobExtractorService,
        ISkillDictionaryService skillDictionaryService,
        IDeterministicScoringService deterministicScoringService,
        IOllamaService ollamaService,
        IConfiguration configuration,
        ILogger<JobRankingService> logger)
    {
        _resumeParserService = resumeParserService;
        _jobExtractorService = jobExtractorService;
        _skillDictionaryService = skillDictionaryService;
        _deterministicScoringService = deterministicScoringService;
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
            resultViewModel.ActiveModel = "Deterministic AI Match Engine (Smart Heuristics)";
            resultViewModel.ActiveEmbeddingModel = "In-Memory Semantic Matcher";
        }

        // 2. Parse Resume
        ResumeData resumeData;
        if (request.DemoMode || (request.ResumeFile == null && request.JobUrls.Count > 0 && request.ManualJdTexts.Count == 0))
        {
            resumeData = GetSampleResumeData();
        }
        else if (request.ResumeFile != null)
        {
            resumeData = await _resumeParserService.ParseResumeAsync(request.ResumeFile, cancellationToken);
        }
        else
        {
            resultViewModel.ErrorMessage = "Please upload a valid resume (PDF or DOCX) or try the demo.";
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

        // 3. Ingest Target Job Descriptions (from URLs and/or manual text inputs)
        var fetchedJobs = new List<JobData>();

        var validUrls = request.JobUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Take(5).ToList();
        if (validUrls.Count == 0 && request.ManualJdTexts.Count == 0 && request.DemoMode)
        {
            validUrls = GetSampleJobUrls();
        }

        if (validUrls.Count > 0)
        {
            var urlJobs = await _jobExtractorService.ExtractMultipleJobsAsync(validUrls, cancellationToken);
            fetchedJobs.AddRange(urlJobs);
        }

        // Handle manual text inputs if any
        if (request.ManualJdTexts != null && request.ManualJdTexts.Count > 0)
        {
            int manualIdx = 1;
            foreach (var manualText in request.ManualJdTexts.Where(t => !string.IsNullOrWhiteSpace(t)).Take(5 - fetchedJobs.Count))
            {
                var clean = manualText.Trim();
                var firstLine = clean.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? $"Custom Job #{manualIdx}";
                
                fetchedJobs.Add(new JobData
                {
                    JobId = $"manual-{manualIdx}",
                    Title = firstLine.Length > 60 ? firstLine[..60] : firstLine,
                    Company = "Direct Text Input",
                    SourceUrl = "#",
                    SourceDomain = "Direct Text",
                    Requirements = clean,
                    Responsibilities = clean,
                    RawFlattenedText = clean,
                    IsSuccess = true,
                    Chunks = new List<TextChunk> { new(clean, "Job Description") }
                });
                manualIdx++;
            }
        }

        if (fetchedJobs.Count == 0)
        {
            resultViewModel.ErrorMessage = "Please provide at least one valid job URL or paste a job description.";
            stopwatch.Stop();
            resultViewModel.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            return resultViewModel;
        }

        resultViewModel.TotalJobsProcessed = fetchedJobs.Count;

        // 4. In-Memory Vector Embeddings (Whole-doc + section embeddings for RAG)
        float[]? resumeWholeVector = null;
        if (resultViewModel.OllamaConnected)
        {
            try
            {
                // Embed resume whole text
                resumeWholeVector = await _ollamaService.GetEmbeddingAsync(resumeData.RawText, resultViewModel.ActiveEmbeddingModel, targetBaseUrl, cancellationToken);

                // Embed resume chunks
                for (int i = 0; i < resumeData.Chunks.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var chunk = resumeData.Chunks[i];
                    chunk.Vector = await _ollamaService.GetEmbeddingAsync(chunk.Text, resultViewModel.ActiveEmbeddingModel, targetBaseUrl, cancellationToken);
                }

                // Embed job texts
                foreach (var job in fetchedJobs.Where(j => j.IsSuccess))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var jobSummary = $"{job.Title} {job.Requirements} {job.Responsibilities}";
                    if (!string.IsNullOrWhiteSpace(jobSummary))
                    {
                        var jobVec = await _ollamaService.GetEmbeddingAsync(jobSummary, resultViewModel.ActiveEmbeddingModel, targetBaseUrl, cancellationToken);
                        if (jobVec != null && job.Chunks.Count > 0)
                        {
                            job.Chunks[0].Vector = jobVec;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vector embedding step encountered an issue, proceeding with semantic keyword extraction.");
            }
        }

        // 5. Extract Structured Profiles (RAG-Grounded)
        var resumeProfile = await _ollamaService.ExtractResumeProfileAsync(resumeData, resultViewModel.ActiveModel, targetBaseUrl, cancellationToken);
        
        // Apply user-confirmed / edited profile overrides from popup if present
        if (!string.IsNullOrWhiteSpace(request.OverriddenCandidateName))
            resumeProfile.CandidateName = request.OverriddenCandidateName.Trim();
        if (!string.IsNullOrWhiteSpace(request.OverriddenTitle))
            resumeProfile.CurrentTitle = request.OverriddenTitle.Trim();
        if (request.OverriddenYearsExperience.HasValue && request.OverriddenYearsExperience.Value >= 0)
            resumeProfile.TotalYearsExperience = request.OverriddenYearsExperience.Value;
        if (!string.IsNullOrWhiteSpace(request.OverriddenDegree))
            resumeProfile.Degree = request.OverriddenDegree.Trim();
        if (!string.IsNullOrWhiteSpace(request.OverriddenSkills))
        {
            var userSkills = request.OverriddenSkills
                .Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (userSkills.Count > 0)
            {
                resumeProfile.Skills = userSkills;
            }
        }

        resultViewModel.CandidateSummary = $"{resumeProfile.CandidateName} ({resumeProfile.CurrentTitle}, {resumeProfile.TotalYearsExperience} yrs exp, {resumeProfile.Skills.Count} skills extracted)";

        // 6. Deterministic Multi-Dimensional Scoring
        var scoringResults = new List<ScoringResult>();
        using var scoringSemaphore = new SemaphoreSlim(2, 2);

        var scoringTasks = fetchedJobs.Select(async job =>
        {
            if (!job.IsSuccess)
            {
                return new ScoringResult
                {
                    JobId = job.JobId,
                    Title = string.IsNullOrWhiteSpace(job.Title) ? "Unavailable Posting" : job.Title,
                    Company = string.IsNullOrWhiteSpace(job.Company) ? job.SourceDomain : job.Company,
                    SourceUrl = job.SourceUrl,
                    SourceDomain = job.SourceDomain,
                    Score = 0,
                    IsSuccess = false,
                    ErrorMessage = job.ErrorMessage ?? "Job listing could not be retrieved."
                };
            }

            await scoringSemaphore.WaitAsync(cancellationToken);
            try
            {
                // A. Extract Structured JD Profile
                var jdProfile = await _ollamaService.ExtractJdProfileAsync(job, resultViewModel.ActiveModel, targetBaseUrl, cancellationToken);

                // B. Compute Semantic Cosine Similarity
                float semanticCosine = 0f;
                var jobVec = job.Chunks.FirstOrDefault(c => c.Vector != null)?.Vector;
                if (resumeWholeVector != null && jobVec != null)
                {
                    semanticCosine = _ollamaService.ComputeCosineSimilarity(resumeWholeVector, jobVec);
                }

                // C. Deterministic Scoring Engine
                var breakdown = _deterministicScoringService.ComputeScore(resumeProfile, jdProfile, semanticCosine);

                // D. Skill Comparison
                var (matchedReq, missingReq) = _skillDictionaryService.CompareSkills(resumeProfile.Skills, jdProfile.RequiredSkills);
                var (matchedNice, _) = _skillDictionaryService.CompareSkills(resumeProfile.Skills, jdProfile.NiceToHaveSkills);

                // E. Experience Gap Description
                string expGap = string.Empty;
                if (jdProfile.MinYearsExperience > 0)
                {
                    if (resumeProfile.TotalYearsExperience >= jdProfile.MinYearsExperience)
                    {
                        expGap = $"{resumeProfile.TotalYearsExperience} yrs (meets {jdProfile.MinYearsExperience} yrs required)";
                    }
                    else
                    {
                        expGap = $"{resumeProfile.TotalYearsExperience} yrs (requires {jdProfile.MinYearsExperience} yrs)";
                    }
                }
                else
                {
                    expGap = $"{resumeProfile.TotalYearsExperience} yrs experience";
                }

                // F. Generate 2-Sentence Recruiter Rationale
                var rationale = await _ollamaService.GenerateMatchRationaleAsync(
                    resumeProfile, 
                    jdProfile, 
                    breakdown, 
                    matchedReq, 
                    missingReq, 
                    resultViewModel.ActiveModel, 
                    targetBaseUrl, 
                    cancellationToken);

                return new ScoringResult
                {
                    JobId = job.JobId,
                    Title = string.IsNullOrWhiteSpace(jdProfile.JobTitle) ? job.Title : jdProfile.JobTitle,
                    Company = string.IsNullOrWhiteSpace(job.Company) ? job.SourceDomain : job.Company,
                    Location = job.Location,
                    Experience = string.IsNullOrWhiteSpace(job.Experience) ? $"{jdProfile.MinYearsExperience} yrs" : job.Experience,
                    Salary = job.Salary,
                    SourceUrl = job.SourceUrl,
                    SourceDomain = job.SourceDomain,
                    Score = breakdown.FinalScore,
                    Breakdown = breakdown,
                    MatchedSkills = matchedReq.Concat(matchedNice).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    MissingRequiredSkills = missingReq,
                    NiceToHaveMatched = matchedNice,
                    ExperienceGapText = expGap,
                    Reasoning = rationale,
                    TopCosineSimilarity = semanticCosine,
                    IsSuccess = true
                };
            }
            finally
            {
                scoringSemaphore.Release();
            }
        });

        var completedScores = await Task.WhenAll(scoringTasks);

        // 7. Server-Side Ranking: Sort highest to lowest score
        resultViewModel.RankedResults = completedScores
            .OrderByDescending(r => r.IsSuccess)
            .ThenByDescending(r => r.Score)
            .ToList();

        stopwatch.Stop();
        resultViewModel.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        return resultViewModel;
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
- Backend: ASP.NET Core, C#, Web API, Minimal APIs, EF Core, Dapper, MediatR, SignalR, RabbitMQ
- Frontend: Angular, React, TypeScript, JavaScript, Bootstrap 5, Tailwind CSS, HTML5, CSS3
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

        var chunks = new List<TextChunk>
        {
            new("RAHIM AHMED - Senior Full Stack Software Engineer with 5+ years experience in ASP.NET Core, C#, SQL Server, Angular, and React.", "SUMMARY"),
            new("Technical Skills: C#, ASP.NET Core, EF Core, SQL Server, PostgreSQL, Redis, Docker, Kubernetes, Angular, React, Ollama, Vector Search, Python.", "SKILLS"),
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
            "https://bdjobs.com/h/details/1519924?ln=1",
            "https://boards.greenhouse.io/anthropic/jobs/4252608007",
            "https://jobs.lever.co/openai/senior-software-engineer",
            "https://jobs.bdjobs.com/jobdetails.asp?id=1358920",
            "https://www.linkedin.com/jobs/view/4012345678"
        };
    }
}
