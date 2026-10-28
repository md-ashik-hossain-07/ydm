using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using YDM.Core;
using YDM.Core.Events;
using YDM.Core.Interfaces;
using YDM.Core.Models;
using YDM.Core.Security;

namespace YDM.Engine;

public class DownloadEngine : IDownloadEngine
{
    private readonly IDownloadRepository _repository;
    private readonly IHttpProbeService _probeService;
    private readonly ISegmentDownloader _segmentDownloader;
    private readonly IFileAssembler _fileAssembler;
    private readonly HttpClient _httpClient;

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeTokens = new();
    private readonly ConcurrentDictionary<Guid, DownloadJob> _activeJobs = new();

    public event EventHandler<JobProgressEventArgs>? ProgressChanged;
    public event EventHandler<JobStateChangedEventArgs>? StateChanged;

    public DownloadEngine(
        IDownloadRepository repository,
        IHttpProbeService? probeService = null,
        ISegmentDownloader? segmentDownloader = null,
        IFileAssembler? fileAssembler = null,
        HttpClient? httpClient = null)
    {
        _repository = repository;
        _probeService = probeService ?? new HttpProbeService();
        _segmentDownloader = segmentDownloader ?? new SegmentDownloader();
        _fileAssembler = fileAssembler ?? new FileAssembler();

        if (httpClient != null)
        {
            _httpClient = httpClient;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 32
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("YDM/1.0 (Windows NT 10.0; Win64; x64)");
        }
    }

    public async Task<DownloadJob> AddDownloadAsync(
        string url, 
        string? destinationDirectory = null, 
        string? customFileName = null, 
        int maxConnections = 8, 
        DownloadPriority priority = DownloadPriority.Normal,
        string? referrer = null,
        string? cookies = null,
        string? userAgent = null,
        DuplicateResolution duplicateResolution = DuplicateResolution.AutoRename,
        CancellationToken ct = default)
    {
        var job = new DownloadJob
        {
            Id = Guid.NewGuid(),
            Url = url,
            Referrer = referrer,
            Cookies = cookies,
            UserAgent = userAgent,
            MaxConnections = Math.Clamp(maxConnections, 1, 32),
            Priority = priority,
            Status = JobStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };

        job.TempDirectory = PathSanitizer.GetTempDirectory(job.Id);

        // Pre-flight probe
        try
        {
            var probe = await _probeService.ProbeAsync(url, referrer, cookies, userAgent, ct);
            if (probe.IsSuccess)
            {
                job.TotalBytes = probe.ContentLength;
                job.IsResumable = probe.SupportsRange;
                job.ETag = probe.ETag;
                job.LastModified = probe.LastModified;

                string resolvedName = !string.IsNullOrWhiteSpace(customFileName) 
                    ? customFileName 
                    : (!string.IsNullOrWhiteSpace(probe.SuggestedFileName) ? probe.SuggestedFileName : "download.dat");

                job.FileName = PathSanitizer.SanitizeFileName(resolvedName);
            }
            else
            {
                // Preserve resolved filename when possible, but do not treat failed probe as successful download
                string fallbackName = !string.IsNullOrWhiteSpace(customFileName)
                    ? customFileName
                    : (!string.IsNullOrWhiteSpace(probe.SuggestedFileName) ? probe.SuggestedFileName : HttpProbeService.ExtractFileName(null, url));

                job.FileName = PathSanitizer.SanitizeFileName(fallbackName);
            }
        }
        catch
        {
            string fallbackName = !string.IsNullOrWhiteSpace(customFileName)
                ? customFileName
                : HttpProbeService.ExtractFileName(null, url);

            job.FileName = PathSanitizer.SanitizeFileName(fallbackName);
        }

        string safeTargetDir = !string.IsNullOrWhiteSpace(destinationDirectory) 
            ? destinationDirectory 
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        job.DestinationPath = PathSanitizer.ResolveDestinationPath(safeTargetDir, job.FileName, duplicateResolution);

        // Create initial segments with adaptive connection sizing (minimum 64 KB per segment)
        int initialConnections = CalculateAdaptiveConnections(job.TotalBytes, job.IsResumable, job.MaxConnections);
        job.Segments = DynamicSegmentSplitter.CreateInitialSegments(job, initialConnections);

        await _repository.SaveJobAsync(job);
        StateChanged?.Invoke(this, new JobStateChangedEventArgs(job.Id, JobStatus.Queued, JobStatus.Queued));

        return job;
    }

    public async Task StartAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _repository.GetByIdAsync(jobId);
        if (job == null) throw new KeyNotFoundException($"Job not found: {jobId}");

        if (job.Status == JobStatus.Completed) return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!_activeTokens.TryAdd(job.Id, cts))
        {
            // Already running
            return;
        }

        _activeJobs[job.Id] = job;
        var token = cts.Token;

        var oldStatus = job.Status;

        // Verify destination directory accessibility, drive readiness, and disk space
        string targetDir = Path.GetDirectoryName(job.DestinationPath) ?? string.Empty;
        var dirValidation = PathSanitizer.ValidateDestinationDirectory(targetDir, job.TotalBytes);
        if (!dirValidation.IsValid)
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = dirValidation.ErrorMessage;
            _activeTokens.TryRemove(job.Id, out _);
            _activeJobs.TryRemove(job.Id, out _);
            cts.Dispose();
            await _repository.UpdateJobStatusAsync(job.Id, JobStatus.Failed, dirValidation.ErrorMessage);
            StateChanged?.Invoke(this, new JobStateChangedEventArgs(job.Id, oldStatus, JobStatus.Failed, dirValidation.ErrorMessage));
            return;
        }

        job.Status = JobStatus.Downloading;
        await _repository.UpdateJobStatusAsync(job.Id, JobStatus.Downloading);
        StateChanged?.Invoke(this, new JobStateChangedEventArgs(job.Id, oldStatus, JobStatus.Downloading));

        try
        {
            // If segments not initialized, initialize now
            if (job.Segments.Count == 0)
            {
                int count = job.IsResumable ? job.MaxConnections : 1;
                job.Segments = DynamicSegmentSplitter.CreateInitialSegments(job, count);
                await _repository.SaveSegmentsAsync(job.Id, job.Segments);
            }

            // Speed calculation variables
            long lastTotalBytes = job.DownloadedBytes;
            var stopwatch = Stopwatch.StartNew();

            var progressReporter = new Progress<long>(_ => { });

            // Timer for regular progress reporting and checkpointing
            using var reportTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            var reportingTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && await reportTimer.WaitForNextTickAsync(token))
                {
                    long currentTotal = job.Segments.Sum(s => s.DownloadedBytes);
                    job.DownloadedBytes = currentTotal;

                    double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                    long deltaBytes = currentTotal - lastTotalBytes;
                    long speed = elapsedSec > 0.1 ? (long)(deltaBytes / elapsedSec) : 0;

                    lastTotalBytes = currentTotal;
                    stopwatch.Restart();

                    job.SpeedBytesPerSecond = speed;
                    if (job.TotalBytes.HasValue && speed > 0)
                    {
                        long remainingBytes = Math.Max(0, job.TotalBytes.Value - currentTotal);
                        job.RemainingTime = TimeSpan.FromSeconds((double)remainingBytes / speed);
                    }
                    else
                    {
                        job.RemainingTime = null;
                    }

                    await _repository.UpdateProgressAsync(job.Id, currentTotal, speed);
                    ProgressChanged?.Invoke(this, new JobProgressEventArgs(
                        job.Id, 
                        currentTotal, 
                        job.TotalBytes, 
                        speed, 
                        job.RemainingTime));
                }
            }, token);

            // Run segment downloads
            var segmentTasks = new List<Task>();
            foreach (var segment in job.Segments.Where(s => !s.IsComplete))
            {
                segmentTasks.Add(RunSegmentWorkerAsync(job, segment, progressReporter, token));
            }

            await Task.WhenAll(segmentTasks);

            // Recheck completeness of all segments
            if (job.Segments.All(s => s.IsComplete))
            {
                // Assembling phase
                job.Status = JobStatus.Assembling;
                await _repository.UpdateJobStatusAsync(job.Id, JobStatus.Assembling);
                StateChanged?.Invoke(this, new JobStateChangedEventArgs(job.Id, JobStatus.Downloading, JobStatus.Assembling));

                await _fileAssembler.AssembleFileAsync(job, ct: token);

                job.Status = JobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;
                job.DownloadedBytes = job.TotalBytes ?? job.Segments.Sum(s => s.DownloadedBytes);
                job.TotalBytes ??= job.DownloadedBytes;
                job.SpeedBytesPerSecond = 0;
                job.RemainingTime = TimeSpan.Zero;

                await _repository.SaveJobAsync(job);
                StateChanged?.Invoke(this, new JobStateChangedEventArgs(job.Id, JobStatus.Assembling, JobStatus.Completed));
            }
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Paused;
            job.SpeedBytesPerSecond = 0;
            job.RemainingTime = null;
            await _repository.UpdateJobStatusAsync(job.Id, JobStatus.Paused);
            StateChanged?.Invoke(this, new JobStateChangedEventArgs(job.Id, JobStatus.Downloading, JobStatus.Paused));
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.SpeedBytesPerSecond = 0;
            job.RemainingTime = null;
            await _repository.UpdateJobStatusAsync(job.Id, JobStatus.Failed, ex.Message);
            StateChanged?.Invoke(this, new JobStateChangedEventArgs(job.Id, JobStatus.Downloading, JobStatus.Failed, ex.Message));
        }
        finally
        {
            _activeTokens.TryRemove(job.Id, out _);
            _activeJobs.TryRemove(job.Id, out _);
            cts.Dispose();
        }
    }

    private async Task RunSegmentWorkerAsync(
        DownloadJob job, 
        DownloadSegment segment, 
        IProgress<long> progress, 
        CancellationToken ct)
    {
        int maxRetries = 3;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await _segmentDownloader.DownloadSegmentAsync(job, segment, _httpClient, progress, ct);
                await _repository.UpdateSegmentProgressAsync(segment.Id, segment.CurrentOffset, segment.Status);
                return;
            }
            catch (OperationCanceledException)
            {
                segment.Status = SegmentStatus.Pending;
                await _repository.UpdateSegmentProgressAsync(segment.Id, segment.CurrentOffset, SegmentStatus.Pending);
                throw;
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode.HasValue)
                {
                    int code = (int)ex.StatusCode.Value;
                    // Do not retry permanent HTTP errors
                    if (code is 400 or 401 or 403 or 404 or 405 or 410)
                    {
                        segment.Status = SegmentStatus.Failed;
                        await _repository.UpdateSegmentProgressAsync(segment.Id, segment.CurrentOffset, SegmentStatus.Failed);
                        throw;
                    }
                }

                if (attempt == maxRetries)
                {
                    segment.Status = SegmentStatus.Failed;
                    await _repository.UpdateSegmentProgressAsync(segment.Id, segment.CurrentOffset, SegmentStatus.Failed);
                    throw;
                }

                int delayMs = ex.StatusCode == (System.Net.HttpStatusCode)429 ? 2000 : 500 * (1 << attempt);
                await Task.Delay(delayMs, ct);
            }
            catch (Exception)
            {
                if (attempt == maxRetries)
                {
                    segment.Status = SegmentStatus.Failed;
                    await _repository.UpdateSegmentProgressAsync(segment.Id, segment.CurrentOffset, SegmentStatus.Failed);
                    throw;
                }

                await Task.Delay(500 * (1 << attempt), ct);
            }
        }
    }

    public Task PauseAsync(Guid jobId)
    {
        if (_activeTokens.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
        }
        return Task.CompletedTask;
    }

    public async Task ResumeAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _repository.GetByIdAsync(jobId);
        if (job == null) throw new KeyNotFoundException($"Job not found: {jobId}");

        if (job.Status == JobStatus.Completed) return;

        // Verify resource on server if resumable
        if (job.IsResumable && !string.IsNullOrWhiteSpace(job.ETag))
        {
            try
            {
                var probe = await _probeService.ProbeAsync(job.Url, job.Referrer, job.Cookies, job.UserAgent, ct);
                if (probe.IsSuccess && !string.IsNullOrWhiteSpace(probe.ETag) && probe.ETag != job.ETag)
                {
                    // ETag changed: invalidate segments and restart from 0
                    job.ETag = probe.ETag;
                    job.DownloadedBytes = 0;
                    job.TotalBytes = probe.ContentLength;
                    job.Segments.Clear();
                    try
                    {
                        if (Directory.Exists(job.TempDirectory))
                        {
                            Directory.Delete(job.TempDirectory, recursive: true);
                        }
                    }
                    catch { }

                    await _repository.SaveJobAsync(job);
                }
            }
            catch { }
        }

        await StartAsync(jobId, ct);
    }

    public Task CancelAsync(Guid jobId)
    {
        if (_activeTokens.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
        }

        _ = Task.Run(async () =>
        {
            await _repository.UpdateJobStatusAsync(jobId, JobStatus.Canceled);
            StateChanged?.Invoke(this, new JobStateChangedEventArgs(jobId, JobStatus.Downloading, JobStatus.Canceled));
        });

        return Task.CompletedTask;
    }

    public async Task RetryAsync(Guid jobId, CancellationToken ct = default)
    {
        await _repository.UpdateJobStatusAsync(jobId, JobStatus.Queued);
        await StartAsync(jobId, ct);
    }

    public async Task RemoveAsync(Guid jobId, bool deleteFiles)
    {
        await PauseAsync(jobId);

        var job = await _repository.GetByIdAsync(jobId);
        if (job != null)
        {
            if (deleteFiles)
            {
                try
                {
                    if (File.Exists(job.DestinationPath))
                    {
                        File.Delete(job.DestinationPath);
                    }
                    if (Directory.Exists(job.TempDirectory))
                    {
                        Directory.Delete(job.TempDirectory, recursive: true);
                    }
                }
                catch { }
            }

            await _repository.DeleteJobAsync(jobId);
        }
    }

    public static int CalculateAdaptiveConnections(long? totalBytes, bool isResumable, int maxConnections)
    {
        if (!isResumable || !totalBytes.HasValue || totalBytes.Value <= 0)
        {
            return 1;
        }

        int maxSegmentsForSize = Math.Max(1, (int)(totalBytes.Value / (64 * 1024)));
        return Math.Clamp(maxConnections, 1, maxSegmentsForSize);
    }
}
