using FindJob.Models;

namespace FindJob.Services;

public interface IBdjobsService
{
    string? ExtractJobId(string input);
    Task<JobData> FetchJobDetailsAsync(string urlOrJobId, CancellationToken cancellationToken = default);
    Task<List<JobData>> FetchMultipleJobsAsync(IEnumerable<string> urlsOrJobIds, CancellationToken cancellationToken = default);
}
