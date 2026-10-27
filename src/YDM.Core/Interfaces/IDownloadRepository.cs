using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using YDM.Core.Models;

namespace YDM.Core.Interfaces;

public interface IDownloadRepository
{
    Task InitializeAsync();
    Task<DownloadJob?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<DownloadJob>> GetAllAsync();
    Task SaveJobAsync(DownloadJob job);
    Task UpdateJobStatusAsync(Guid id, JobStatus status, string? errorMessage = null);
    Task UpdateProgressAsync(Guid id, long downloadedBytes, long speedBytesPerSecond = 0);
    Task DeleteJobAsync(Guid id);
    Task SaveSegmentsAsync(Guid jobId, IEnumerable<DownloadSegment> segments);
    Task<IReadOnlyList<DownloadSegment>> GetSegmentsAsync(Guid jobId);
    Task UpdateSegmentProgressAsync(Guid segmentId, long currentOffset, SegmentStatus status);
}

public interface ISettingsRepository
{
    Task InitializeAsync();
    Task<ApplicationSettings> GetSettingsAsync();
    Task SaveSettingsAsync(ApplicationSettings settings);
}
