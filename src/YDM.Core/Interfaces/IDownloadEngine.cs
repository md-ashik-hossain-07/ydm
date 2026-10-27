using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using YDM.Core.Events;
using YDM.Core.Models;

namespace YDM.Core.Interfaces;

public interface IHttpProbeService
{
    Task<ProbeResult> ProbeAsync(string url, string? referrer = null, string? cookies = null, string? userAgent = null, CancellationToken ct = default);
}

public interface ISegmentDownloader
{
    Task DownloadSegmentAsync(
        DownloadJob job, 
        DownloadSegment segment, 
        HttpClient httpClient, 
        IProgress<long> progress, 
        CancellationToken ct);
}

public interface IFileAssembler
{
    Task<bool> AssembleFileAsync(
        DownloadJob job, 
        IProgress<double>? progress = null, 
        CancellationToken ct = default);
}

public interface IDownloadEngine
{
    event EventHandler<JobProgressEventArgs>? ProgressChanged;
    event EventHandler<JobStateChangedEventArgs>? StateChanged;

    Task<DownloadJob> AddDownloadAsync(
        string url, 
        string? destinationDirectory = null, 
        string? customFileName = null, 
        int maxConnections = 8, 
        DownloadPriority priority = DownloadPriority.Normal,
        string? referrer = null,
        string? cookies = null,
        string? userAgent = null,
        CancellationToken ct = default);

    Task StartAsync(Guid jobId, CancellationToken ct = default);
    Task PauseAsync(Guid jobId);
    Task ResumeAsync(Guid jobId, CancellationToken ct = default);
    Task CancelAsync(Guid jobId);
    Task RetryAsync(Guid jobId, CancellationToken ct = default);
    Task RemoveAsync(Guid jobId, bool deleteFiles);
}
