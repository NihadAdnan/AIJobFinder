using System.Diagnostics;
using FindJob.Models;
using FindJob.Services;
using Microsoft.AspNetCore.Mvc;

namespace FindJob.Controllers;

public class HomeController : Controller
{
    private readonly IJobRankingService _jobRankingService;
    private readonly IResumeParserService _resumeParserService;
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IJobRankingService jobRankingService,
        IResumeParserService resumeParserService,
        IOllamaService ollamaService,
        ILogger<HomeController> logger)
    {
        _jobRankingService = jobRankingService;
        _resumeParserService = resumeParserService;
        _ollamaService = ollamaService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new JobFinderResultViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> Analyze(JobFinderRequestViewModel model, CancellationToken cancellationToken)
    {
        // Filter out empty URL inputs
        model.JobUrls = model.JobUrls.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();

        var result = await _jobRankingService.ProcessAndRankJobsAsync(model, cancellationToken);

        // If AJAX request, return JSON for seamless dynamic UI updates
        if (Request.Headers.XRequestedWith == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json"))
        {
            return Json(new
            {
                success = string.IsNullOrEmpty(result.ErrorMessage),
                errorMessage = result.ErrorMessage,
                data = result
            });
        }

        // Otherwise return view
        return View("Index", result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ParseResumePreview(IFormFile? resumeFile, [FromForm] string? customBaseUrl, [FromForm] string? customModel, CancellationToken cancellationToken)
    {
        if (resumeFile == null || resumeFile.Length == 0)
        {
            return Json(new { success = false, errorMessage = "No resume file was provided." });
        }

        try
        {
            var resumeData = await _resumeParserService.ParseResumeAsync(resumeFile, cancellationToken);
            if (!resumeData.IsSuccess)
            {
                return Json(new { success = false, errorMessage = resumeData.ErrorMessage ?? "Failed to parse resume." });
            }

            var profile = await _ollamaService.ExtractResumeProfileAsync(resumeData, customModel, customBaseUrl, cancellationToken);

            return Json(new
            {
                success = true,
                candidateName = profile.CandidateName,
                currentTitle = profile.CurrentTitle,
                totalYearsExperience = profile.TotalYearsExperience,
                degree = profile.Degree,
                skills = profile.Skills,
                skillsString = string.Join(", ", profile.Skills)
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract resume preview");
            return Json(new { success = false, errorMessage = "Could not parse resume profile." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> CheckOllamaStatus([FromQuery] string? baseUrl, CancellationToken cancellationToken)
    {
        var status = await _ollamaService.CheckHealthAsync(baseUrl, cancellationToken);
        return Json(status);
    }

    [HttpGet]
    public IActionResult GetSamplePresets()
    {
        return Json(new
        {
            urls = new[]
            {
                "https://bdjobs.com/h/details/1519924?ln=1",
                "https://boards.greenhouse.io/anthropic/jobs/4252608007",
                "https://jobs.lever.co/openai/senior-software-engineer",
                "https://jobs.bdjobs.com/jobdetails.asp?id=1358920",
                "https://www.linkedin.com/jobs/view/full-stack-engineer-4012345678"
            },
            candidateName = "Rahim Ahmed (.NET & AI Software Engineer)"
        });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
