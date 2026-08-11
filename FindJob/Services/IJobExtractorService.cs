using FindJob.Models;

namespace FindJob.Services;

public interface IJobExtractorService
{
    Task<JobData> ExtractJobAsync(string url, CancellationToken cancellationToken = default);
    Task<List<JobData>> ExtractMultipleJobsAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default);
    string ExtractDomainLabel(string url);
}
