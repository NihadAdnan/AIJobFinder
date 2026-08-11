using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using FindJob.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FindJob.Services;

public partial class BdjobsService : IBdjobsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BdjobsService> _logger;
    private readonly string _gatewayBaseUrl;
    private readonly string _userAgent;

    public BdjobsService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<BdjobsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _gatewayBaseUrl = configuration["Bdjobs:GatewayBaseUrl"] 
            ?? "https://gateway.bdjobs.com/jobapply/api/JobSubsystem/Job-Details";
        _userAgent = configuration["Bdjobs:UserAgent"] 
            ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";
    }

    public string? ExtractJobId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.Trim();

        // 1. Pure numeric digits (e.g. "1519924")
        if (NumericDigitsRegex().IsMatch(trimmed))
        {
            return trimmed;
        }

        // 2. Query param: id=1519924 or jobId=1519924 or ln=1&id=1519924
        var queryMatch = JobIdQueryRegex().Match(trimmed);
        if (queryMatch.Success)
        {
            return queryMatch.Groups[1].Value;
        }

        // 3. Path patterns: /h/details/1519924, /details/1519924, /jobdetails/1519924, /jobs/1519924
        var pathMatch = PathJobIdRegex().Match(trimmed);
        if (pathMatch.Success)
        {
            return pathMatch.Groups[1].Value;
        }

        // 4. Universal fallback: Extract any 5 to 10 digit number in the input
        var generalMatch = AnyIdRegex().Match(trimmed);
        if (generalMatch.Success)
        {
            return generalMatch.Groups[1].Value;
        }

        return null;
    }

    public async Task<JobData> FetchJobDetailsAsync(string urlOrJobId, CancellationToken cancellationToken = default)
    {
        var jobData = new JobData
        {
            SourceUrl = urlOrJobId.Trim()
        };

        var jobId = ExtractJobId(urlOrJobId);
        if (string.IsNullOrEmpty(jobId))
        {
            jobData.IsSuccess = false;
            jobData.ErrorMessage = $"Could not extract valid Bdjobs Job ID from: '{urlOrJobId}'";
            return jobData;
        }

        jobData.JobId = jobId;
        if (string.IsNullOrEmpty(jobData.SourceUrl) || !jobData.SourceUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            jobData.SourceUrl = $"https://jobs.bdjobs.com/jobdetails.asp?id={jobId}";
        }

        try
        {
            var client = _httpClientFactory.CreateClient("BdjobsClient");
            
            // Build the gateway URL with jobId, ln=1, and IsCorporate=false
            var baseUrlWithoutQuery = _gatewayBaseUrl.Split('?')[0];
            var requestUrl = $"{baseUrlWithoutQuery}?jobId={jobId}&ln=1&IsCorporate=false";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.UserAgent.ParseAdd(_userAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                jobData.IsSuccess = false;
                jobData.ErrorMessage = $"Bdjobs gateway returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) for Job ID #{jobId}.";
                return jobData;
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(jsonContent) || jsonContent.Trim() == "null" || jsonContent.Trim() == "[]")
            {
                jobData.IsSuccess = false;
                jobData.ErrorMessage = $"No job details returned for Job ID #{jobId} (the listing may have expired or been removed).";
                return jobData;
            }

            PopulateJobDataFromJson(jobData, jsonContent);
            jobData.IsSuccess = true;
        }
        catch (OperationCanceledException)
        {
            jobData.IsSuccess = false;
            jobData.ErrorMessage = $"Request timed out while fetching Job ID #{jobId}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Bdjobs data for Job ID: {JobId}", jobId);
            jobData.IsSuccess = false;
            jobData.ErrorMessage = $"Error fetching job data: {ex.Message}";
        }

        return jobData;
    }

    public async Task<List<JobData>> FetchMultipleJobsAsync(IEnumerable<string> urlsOrJobIds, CancellationToken cancellationToken = default)
    {
        var cleanedInputs = urlsOrJobIds
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        if (cleanedInputs.Count == 0)
        {
            return new List<JobData>();
        }

        // Deduplicate by extracted Job ID to avoid redundant HTTP requests
        var uniqueJobMap = new Dictionary<string, string>(); // JobId -> OriginalUrl
        var invalidInputs = new List<string>();

        foreach (var input in cleanedInputs)
        {
            var jobId = ExtractJobId(input);
            if (jobId != null)
            {
                if (!uniqueJobMap.ContainsKey(jobId))
                {
                    uniqueJobMap[jobId] = input;
                }
            }
            else
            {
                invalidInputs.Add(input);
            }
        }

        var results = new List<JobData>();

        // Add invalid ones directly
        foreach (var invalid in invalidInputs)
        {
            results.Add(new JobData
            {
                SourceUrl = invalid,
                IsSuccess = false,
                ErrorMessage = $"Invalid Bdjobs URL or Job ID: '{invalid}'"
            });
        }

        // Fetch valid jobs concurrently with bounded degree of parallelism (max 3 concurrent)
        using var semaphore = new SemaphoreSlim(3, 3);
        var tasks = uniqueJobMap.Select(async kvp =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await FetchJobDetailsAsync(kvp.Value, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var fetched = await Task.WhenAll(tasks);
        results.AddRange(fetched);

        return results;
    }

    private void PopulateJobDataFromJson(JobData jobData, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement targetElement = root;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                targetElement = root[0];
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("data", out var dataProp))
                {
                    if (dataProp.ValueKind == JsonValueKind.Array && dataProp.GetArrayLength() > 0)
                    {
                        targetElement = dataProp[0];
                    }
                    else if (dataProp.ValueKind == JsonValueKind.Object)
                    {
                        targetElement = dataProp;
                    }
                }
            }

            jobData.Title = CleanHtml(GetElementProperty(targetElement, "JobTitle", "jobTitle", "title", "position"));
            jobData.Company = CleanHtml(GetElementProperty(targetElement, "CompanyName", "CompanyNameENG", "companyName", "company", "orgName"));
            jobData.Location = CleanHtml(GetElementProperty(targetElement, "JobLocation", "jobLoc", "location", "companyAddress", "CompanyAddress"));
            jobData.JobNature = CleanHtml(GetElementProperty(targetElement, "JobNature", "jobNature", "employmentType", "nature"));
            jobData.Workplace = CleanHtml(GetElementProperty(targetElement, "JobWorkPlace", "workplace", "Workplace"));
            jobData.Experience = CleanHtml(GetElementProperty(targetElement, "experience", "expReq", "ExpReq", "minExp"));
            jobData.Salary = CleanHtml(GetElementProperty(targetElement, "JobSalaryRange", "JobSalaryRangeText", "salary", "Salary", "minSalary"));
            jobData.Deadline = CleanHtml(GetElementProperty(targetElement, "Deadline", "deadline", "PublishDate", "publishDate"));

            jobData.Education = CleanHtml(GetElementProperty(targetElement, "EducationRequirements", "eduReq", "EduReq", "qualification"));
            
            var skills = GetElementProperty(targetElement, "SuggestedSkills", "SkillsRequired", "skills", "skillReq", "Skills");
            var addReq = GetElementProperty(targetElement, "AdditionJobRequirements", "addReq", "AddReq", "additionalRequirements");
            jobData.Requirements = CleanHtml($"{skills} {addReq}".Trim());
            
            jobData.Responsibilities = CleanHtml(GetElementProperty(targetElement, "JobDescription", "jobResp", "JobResp", "JobContext", "responsibilities"));
            jobData.AdditionalRequirements = CleanHtml(addReq);
            jobData.OtherBenefits = CleanHtml(GetElementProperty(targetElement, "JobOtherBenifits", "otherBenifits", "OtherBenefits", "benefits"));

            jobData.RawFlattenedText = jobData.ToFormattedContext();

            // Create section chunks for RAG
            jobData.Chunks = CreateJobChunks(jobData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON schema for Job #{JobId}, storing raw text", jobData.JobId);
            jobData.RawFlattenedText = CleanHtml(json);
            jobData.Chunks.Add(new TextChunk(jobData.RawFlattenedText, "Job Posting"));
        }
    }

    private static List<TextChunk> CreateJobChunks(JobData job)
    {
        var chunks = new List<TextChunk>();

        if (!string.IsNullOrWhiteSpace(job.Title) || !string.IsNullOrWhiteSpace(job.Company))
        {
            chunks.Add(new TextChunk($"Role: {job.Title} at {job.Company}. Experience: {job.Experience}. Location: {job.Location}", "Overview"));
        }

        if (!string.IsNullOrWhiteSpace(job.Requirements) || !string.IsNullOrWhiteSpace(job.Education))
        {
            chunks.Add(new TextChunk($"Requirements & Skills: {job.Requirements}\nEducation: {job.Education}", "Requirements"));
        }

        if (!string.IsNullOrWhiteSpace(job.Responsibilities))
        {
            chunks.Add(new TextChunk($"Responsibilities: {job.Responsibilities}", "Responsibilities"));
        }

        if (!string.IsNullOrWhiteSpace(job.AdditionalRequirements))
        {
            chunks.Add(new TextChunk($"Additional Requirements: {job.AdditionalRequirements}", "Additional Requirements"));
        }

        if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(job.RawFlattenedText))
        {
            chunks.Add(new TextChunk(job.RawFlattenedText, "Job Description"));
        }

        return chunks;
    }

    private static string GetElementProperty(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object) return string.Empty;

        foreach (var prop in propertyNames)
        {
            if (element.TryGetProperty(prop, out var val))
            {
                var str = val.ToString();
                if (!string.IsNullOrWhiteSpace(str) && str != "null")
                {
                    return str;
                }
            }
        }

        return string.Empty;
    }

    private static string CleanHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Replace line break and list item elements with newlines/bullets
            foreach (var node in doc.DocumentNode.SelectNodes("//br|//p|//div") ?? Enumerable.Empty<HtmlNode>())
            {
                node.ParentNode.ReplaceChild(HtmlNode.CreateNode("\n"), node);
            }
            foreach (var li in doc.DocumentNode.SelectNodes("//li") ?? Enumerable.Empty<HtmlNode>())
            {
                li.ParentNode.ReplaceChild(HtmlNode.CreateNode($"\n• {li.InnerText.Trim()}"), li);
            }

            var text = HtmlEntity.DeEntitize(doc.DocumentNode.InnerText);
            text = text.Replace("\r", "");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }
        catch
        {
            // Fallback plain regex tag stripping
            var stripped = Regex.Replace(html, "<.*?>", " ");
            return HtmlEntity.DeEntitize(stripped).Trim();
        }
    }

    [GeneratedRegex(@"^\d{4,10}$")]
    private static partial Regex NumericDigitsRegex();

    [GeneratedRegex(@"(?:[?&](?:job)?id=)(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex JobIdQueryRegex();

    [GeneratedRegex(@"/(?:(?:[a-zA-Z0-9_-]+/)?(?:details|jobs|jobdetails)/)(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PathJobIdRegex();

    [GeneratedRegex(@"\b(\d{5,10})\b")]
    private static partial Regex AnyIdRegex();
}
