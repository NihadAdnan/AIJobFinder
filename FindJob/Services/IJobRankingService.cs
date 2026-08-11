using FindJob.Models;

namespace FindJob.Services;

public interface IJobRankingService
{
    Task<JobFinderResultViewModel> ProcessAndRankJobsAsync(JobFinderRequestViewModel request, CancellationToken cancellationToken = default);
}
