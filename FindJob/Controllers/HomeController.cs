using System.Diagnostics;
using FindJob.Models;
using FindJob.Services;
using Microsoft.AspNetCore.Mvc;

namespace FindJob.Controllers;

public class HomeController : Controller
{
    private readonly IJobRankingService _jobRankingService;
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IJobRankingService jobRankingService,
        IOllamaService ollamaService,
        ILogger<HomeController> logger)
    {
        _jobRankingService = jobRankingService;
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
