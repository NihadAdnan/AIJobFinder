using FindJob.Models;
using Microsoft.AspNetCore.Http;

namespace FindJob.Services;

public interface IResumeParserService
{
    Task<ResumeData> ParseResumeAsync(IFormFile file, CancellationToken cancellationToken = default);
    ResumeData ParseText(string text, string fileName = "sample_resume.txt");
}
