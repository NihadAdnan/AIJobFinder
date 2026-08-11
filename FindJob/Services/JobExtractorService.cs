using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FindJob.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace FindJob.Services;

public class JobExtractorService : IJobExtractorService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBdjobsService _bdjobsService;
    private readonly ILogger<JobExtractorService> _logger;

    private const string BrowserUserAgent = 
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    public JobExtractorService(
        IHttpClientFactory httpClientFactory, 
        IBdjobsService bdjobsService, 
        ILogger<JobExtractorService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _bdjobsService = bdjobsService;
        _logger = logger;
    }

    public string ExtractDomainLabel(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Web";

        try
        {
            var trimmed = url.Trim();
            // Only purely numeric inputs (e.g. "1519924") represent standalone Bdjobs IDs
            if (Regex.IsMatch(trimmed, @"^\d{4,10}$")) return "Bdjobs";

            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "https://" + trimmed;
            }

            var uri = new Uri(trimmed);
            var host = uri.Host.ToLowerInvariant();

            if (host.Contains("bdjobs.com")) return "Bdjobs";
            if (host.Contains("linkedin.com")) return "LinkedIn";
            if (host.Contains("greenhouse.io")) return "Greenhouse";
            if (host.Contains("lever.co")) return "Lever";
            if (host.Contains("indeed.com")) return "Indeed";
            if (host.Contains("workable.com")) return "Workable";
            if (host.Contains("ashbyhq.com")) return "Ashby";
            if (host.Contains("smartrecruiters.com")) return "SmartRecruiters";
            if (host.Contains("glassdoor.com")) return "Glassdoor";
            if (host.Contains("wellfound.com") || host.Contains("angel.co")) return "Wellfound";
            if (host.Contains("remoteok.com") || host.Contains("weworkremotely.com")) return "RemoteOK";

            // Clean host e.g. "careers.google.com" -> "Google", "jobs.apple.com" -> "Apple"
            var parts = host.Replace("www.", "").Replace("jobs.", "").Replace("careers.", "").Replace("boards.", "").Split('.');
            if (parts.Length > 0 && parts[0].Length > 1)
            {
                return char.ToUpper(parts[0][0]) + parts[0][1..];
            }

            return host;
        }
        catch
        {
            return "Web";
        }
    }

    public async Task<JobData> ExtractJobAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new JobData { IsSuccess = false, ErrorMessage = "Please enter a valid job URL." };
        }

        var trimmedUrl = url.Trim();
        var domainLabel = ExtractDomainLabel(trimmedUrl);

        // 1. Route to dedicated Bdjobs API gateway ONLY if URL is explicitly from bdjobs.com or is a bare numeric ID
        bool isExplicitBdjobs = domainLabel == "Bdjobs" || 
                                trimmedUrl.Contains("bdjobs.com", StringComparison.OrdinalIgnoreCase) || 
                                Regex.IsMatch(trimmedUrl, @"^\d{4,10}$");

        if (isExplicitBdjobs)
        {
            var bdjobsResult = await _bdjobsService.FetchJobDetailsAsync(trimmedUrl, cancellationToken);
            bdjobsResult.SourceDomain = "Bdjobs";
            if (!bdjobsResult.IsSuccess && !string.IsNullOrWhiteSpace(bdjobsResult.ErrorMessage))
            {
                bdjobsResult.ErrorMessage = $"Unable to retrieve posting from Bdjobs (Listing #{bdjobsResult.JobId} may be expired or unavailable).";
            }
            return bdjobsResult;
        }

        // 2. Fetch and parse arbitrary webpage (Greenhouse, LinkedIn, Lever, Indeed, Career Sites)
        var jobData = new JobData
        {
            SourceUrl = trimmedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? trimmedUrl : $"https://{trimmedUrl}",
            SourceDomain = domainLabel,
            JobId = GenerateJobIdFromUrl(trimmedUrl)
        };

        try
        {
            var client = _httpClientFactory.CreateClient("UniversalWebClient");
            using var request = new HttpRequestMessage(HttpMethod.Get, jobData.SourceUrl);
            request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.9));

            var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                jobData.IsSuccess = false;
                var code = (int)response.StatusCode;
                jobData.ErrorMessage = code switch
                {
                    404 => $"The job posting on {domainLabel} was not found (the listing may have been closed or removed).",
                    403 or 401 => $"{domainLabel} requires login or restricted automated access. You can try viewing it directly in your browser.",
                    429 => $"{domainLabel} rate limited the request. Please try again in a few moments.",
                    _ => $"Unable to load job details from {domainLabel} (Server returned HTTP {code})."
                };
                return jobData;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
            {
                jobData.IsSuccess = false;
                jobData.ErrorMessage = $"Received empty content from {domainLabel}.";
                return jobData;
            }

            ParseGenericJobWebpage(jobData, html);
            jobData.IsSuccess = true;
        }
        catch (OperationCanceledException)
        {
            jobData.IsSuccess = false;
            jobData.ErrorMessage = $"Connection to {domainLabel} timed out. The career site took too long to respond.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scrape job URL: {Url}", trimmedUrl);
            jobData.IsSuccess = false;
            jobData.ErrorMessage = $"Unable to extract job details from {domainLabel} ({ex.Message}).";
        }

        return jobData;
    }

    public async Task<List<JobData>> ExtractMultipleJobsAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default)
    {
        var cleanedUrls = urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        if (cleanedUrls.Count == 0)
        {
            return new List<JobData>();
        }

        using var semaphore = new SemaphoreSlim(3, 3);
        var tasks = cleanedUrls.Select(async url =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await ExtractJobAsync(url, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private void ParseGenericJobWebpage(JobData jobData, string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // A. Attempt JSON-LD Schema.org/JobPosting extraction
        bool jsonLdFound = TryExtractJsonLdJobPosting(jobData, doc);

        // B. Extract OpenGraph / Meta tags for missing fields
        ExtractMetaTags(jobData, doc);

        // C. Clean DOM and extract main readable content
        CleanDomNodes(doc.DocumentNode);
        var mainText = ExtractMainContentText(doc.DocumentNode);

        if (string.IsNullOrWhiteSpace(jobData.Responsibilities) && string.IsNullOrWhiteSpace(jobData.Requirements))
        {
            jobData.Responsibilities = mainText;
            jobData.Requirements = mainText;
        }

        if (string.IsNullOrWhiteSpace(jobData.Title))
        {
            jobData.Title = ExtractTitleFromDom(doc.DocumentNode) ?? $"{jobData.SourceDomain} Role";
        }

        if (string.IsNullOrWhiteSpace(jobData.Company))
        {
            jobData.Company = jobData.SourceDomain;
        }

        jobData.RawFlattenedText = jobData.ToFormattedContext();
        jobData.Chunks = CreateJobChunks(jobData);
    }

    private static bool TryExtractJsonLdJobPosting(JobData jobData, HtmlDocument doc)
    {
        var scriptNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scriptNodes == null) return false;

        foreach (var node in scriptNodes)
        {
            var json = node.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(json)) continue;

            try
            {
                using var jsonDoc = JsonDocument.Parse(json);
                var root = jsonDoc.RootElement;

                // Handle single object or array or @graph array
                var targetElements = new List<JsonElement>();
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
                    {
                        targetElements.AddRange(graph.EnumerateArray());
                    }
                    else
                    {
                        targetElements.Add(root);
                    }
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    targetElements.AddRange(root.EnumerateArray());
                }

                foreach (var el in targetElements)
                {
                    if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("@type", out var typeProp))
                    {
                        var typeStr = typeProp.ToString();
                        if (typeStr.Contains("JobPosting", StringComparison.OrdinalIgnoreCase))
                        {
                            if (el.TryGetProperty("title", out var titleVal))
                                jobData.Title = CleanText(titleVal.GetString());

                            if (el.TryGetProperty("hiringOrganization", out var orgVal) && orgVal.ValueKind == JsonValueKind.Object)
                            {
                                if (orgVal.TryGetProperty("name", out var orgName))
                                    jobData.Company = CleanText(orgName.GetString());
                            }

                            if (el.TryGetProperty("description", out var descVal))
                                jobData.Responsibilities = StripHtmlTags(descVal.GetString() ?? "");

                            if (el.TryGetProperty("employmentType", out var empVal))
                                jobData.JobNature = CleanText(empVal.ToString());

                            if (el.TryGetProperty("skills", out var skillsVal))
                                jobData.Requirements = CleanText(skillsVal.ToString());

                            if (el.TryGetProperty("qualifications", out var qualVal))
                                jobData.Education = CleanText(qualVal.ToString());

                            if (el.TryGetProperty("baseSalary", out var salVal))
                                jobData.Salary = CleanText(salVal.ToString());

                            if (el.TryGetProperty("jobLocation", out var locVal))
                            {
                                if (locVal.ValueKind == JsonValueKind.Object && locVal.TryGetProperty("address", out var addrVal))
                                {
                                    jobData.Location = CleanText(addrVal.ToString());
                                }
                                else
                                {
                                    jobData.Location = CleanText(locVal.ToString());
                                }
                            }

                            return true;
                        }
                    }
                }
            }
            catch { }
        }

        return false;
    }

    private static void ExtractMetaTags(JobData jobData, HtmlDocument doc)
    {
        if (string.IsNullOrWhiteSpace(jobData.Title))
        {
            var ogTitle = GetMetaContent(doc, "og:title", "twitter:title");
            if (!string.IsNullOrWhiteSpace(ogTitle))
            {
                jobData.Title = CleanText(ogTitle);
            }
        }

        if (string.IsNullOrWhiteSpace(jobData.Company))
        {
            var ogSite = GetMetaContent(doc, "og:site_name", "author", "publisher");
            if (!string.IsNullOrWhiteSpace(ogSite))
            {
                jobData.Company = CleanText(ogSite);
            }
        }

        if (string.IsNullOrWhiteSpace(jobData.Requirements))
        {
            var ogDesc = GetMetaContent(doc, "og:description", "description", "twitter:description");
            if (!string.IsNullOrWhiteSpace(ogDesc))
            {
                jobData.Requirements = CleanText(ogDesc);
            }
        }
    }

    private static string? GetMetaContent(HtmlDocument doc, params string[] propertyNames)
    {
        foreach (var prop in propertyNames)
        {
            var node = doc.DocumentNode.SelectSingleNode($"//meta[@property='{prop}' or @name='{prop}']");
            var content = node?.GetAttributeValue("content", string.Empty);
            if (!string.IsNullOrWhiteSpace(content)) return content;
        }
        return null;
    }

    private static void CleanDomNodes(HtmlNode root)
    {
        var noisyNodes = root.SelectNodes(
            "//script|//style|//nav|//header|//footer|//aside|//noscript|//iframe|//svg|//button|//form|//*[contains(@class, 'cookie')]|//*[contains(@class, 'navbar')]|//*[contains(@class, 'footer')]|//*[contains(@id, 'footer')]")
            ?? Enumerable.Empty<HtmlNode>();

        foreach (var node in noisyNodes.ToList())
        {
            node.Remove();
        }
    }

    private static string ExtractMainContentText(HtmlNode root)
    {
        var mainContainer = root.SelectSingleNode(
            "//article|//main|//*[@role='main']|//*[contains(@class, 'job-description')]|//*[contains(@class, 'job-details')]|//*[contains(@id, 'job-description')]|//*[contains(@id, 'job-details')]|//*[contains(@class, 'description')]")
            ?? root.SelectSingleNode("//body") 
            ?? root;

        foreach (var li in mainContainer.SelectNodes(".//li") ?? Enumerable.Empty<HtmlNode>())
        {
            li.ParentNode?.ReplaceChild(HtmlNode.CreateNode($"\n• {li.InnerText.Trim()}"), li);
        }

        foreach (var p in mainContainer.SelectNodes(".//p|.//br|.//div|.//h1|.//h2|.//h3|.//h4|.//h5") ?? Enumerable.Empty<HtmlNode>())
        {
            p.ParentNode?.ReplaceChild(HtmlNode.CreateNode($"\n{p.InnerText.Trim()}\n"), p);
        }

        var text = HtmlEntity.DeEntitize(mainContainer.InnerText);
        text = Regex.Replace(text, @"\r\n|\r", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        text = Regex.Replace(text, @"[ \t]{2,}", " ");

        var trimmed = text.Trim();
        return trimmed.Length > 8000 ? trimmed[..8000] : trimmed;
    }

    private static string? ExtractTitleFromDom(HtmlNode root)
    {
        var h1 = root.SelectSingleNode("//h1");
        if (h1 != null && !string.IsNullOrWhiteSpace(h1.InnerText))
        {
            var text = CleanText(h1.InnerText);
            if (text.Length <= 100) return text;
        }

        var titleTag = root.SelectSingleNode("//title");
        if (titleTag != null && !string.IsNullOrWhiteSpace(titleTag.InnerText))
        {
            var clean = CleanText(titleTag.InnerText);
            var parts = clean.Split(new[] { '|', '-', '•', '–' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0) return parts[0];
        }

        return null;
    }

    private static List<TextChunk> CreateJobChunks(JobData job)
    {
        var chunks = new List<TextChunk>();

        if (!string.IsNullOrWhiteSpace(job.Title) || !string.IsNullOrWhiteSpace(job.Company))
        {
            chunks.Add(new TextChunk($"Role: {job.Title} at {job.Company}. Source: {job.SourceDomain}. Location: {job.Location}", "Overview"));
        }

        if (!string.IsNullOrWhiteSpace(job.Requirements) || !string.IsNullOrWhiteSpace(job.Education))
        {
            chunks.Add(new TextChunk($"Requirements & Qualifications: {job.Requirements}\nEducation: {job.Education}", "Requirements"));
        }

        if (!string.IsNullOrWhiteSpace(job.Responsibilities))
        {
            var resp = job.Responsibilities;
            if (resp.Length > 800)
            {
                var paragraphs = resp.Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var buf = new StringBuilder();
                foreach (var p in paragraphs)
                {
                    if (buf.Length + p.Length > 550 && buf.Length > 0)
                    {
                        chunks.Add(new TextChunk(buf.ToString().Trim(), "Responsibilities"));
                        buf.Clear();
                    }
                    buf.AppendLine(p);
                }
                if (buf.Length > 0) chunks.Add(new TextChunk(buf.ToString().Trim(), "Responsibilities"));
            }
            else
            {
                chunks.Add(new TextChunk($"Responsibilities:\n{resp}", "Responsibilities"));
            }
        }

        if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(job.RawFlattenedText))
        {
            chunks.Add(new TextChunk(job.RawFlattenedText, "Job Description"));
        }

        return chunks;
    }

    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        foreach (var li in doc.DocumentNode.SelectNodes("//li") ?? Enumerable.Empty<HtmlNode>())
        {
            li.ParentNode?.ReplaceChild(HtmlNode.CreateNode($"\n• {li.InnerText.Trim()}"), li);
        }
        var text = HtmlEntity.DeEntitize(doc.DocumentNode.InnerText);
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string CleanText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var decoded = HtmlEntity.DeEntitize(input);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static string GenerateJobIdFromUrl(string url)
    {
        var match = Regex.Match(url, @"\b(\d{5,10})\b");
        if (match.Success) return match.Groups[1].Value;

        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }
}
