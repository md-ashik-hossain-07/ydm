using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YDM.Core;
using YDM.Core.Events;
using YDM.Core.Interfaces;
using YDM.Core.Models;

namespace YDM.Queue;

public interface IDownloadQueueManager
{
    bool IsRunning { get; }
    int ActiveDownloadsCount { get; }
    int MaxConcurrency { get; set; }

    event EventHandler? QueueUpdated;

    Task StartQueueAsync(CancellationToken ct = default);
    Task StopQueueAsync();
    Task EnqueueJobAsync(Guid jobId);
    Task PauseJobAsync(Guid jobId);
    Task ResumeJobAsync(Guid jobId);
    Task CancelJobAsync(Guid jobId);
    Task RemoveJobAsync(Guid jobId, bool deleteFiles);
}

public class DownloadQueueManager : IDownloadQueueManager
{
    private readonly IDownloadEngine _engine;
    private readonly IDownloadRepository _repository;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly ConcurrentDictionary<Guid, Task> _runningTasks = new();
    
    private CancellationTokenSource? _queueCts;
    private int _maxConcurrency = 2;
    private bool _isRunning;

    public bool IsRunning => _isRunning;
    public int ActiveDownloadsCount => _runningTasks.Count;

    public int MaxConcurrency
    {
        get => _maxConcurrency;
        set => _maxConcurrency = Math.Clamp(value, 1, 10);
    }

    public event EventHandler? QueueUpdated;

    public DownloadQueueManager(IDownloadEngine engine, IDownloadRepository repository, int maxConcurrency = 2)
    {
        _engine = engine;
        _repository = repository;
        _maxConcurrency = Math.Clamp(maxConcurrency, 1, 10);
        _concurrencySemaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);

        _engine.StateChanged += OnEngineStateChanged;
    }

    public Task StartQueueAsync(CancellationToken ct = default)
    {
        if (_isRunning) return Task.CompletedTask;

        _isRunning = true;
        _queueCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        QueueUpdated?.Invoke(this, EventArgs.Empty);

        _ = Task.Run(() => ProcessQueueLoopAsync(_queueCts.Token), _queueCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopQueueAsync()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _queueCts?.Cancel();
        _queueCts?.Dispose();
        _queueCts = null;

        // Pause all actively running engine jobs
        var activeIds = _runningTasks.Keys.ToList();
        foreach (var id in activeIds)
        {
            await _engine.PauseAsync(id);
        }

        QueueUpdated?.Invoke(this, EventArgs.Empty);
    }

    public async Task EnqueueJobAsync(Guid jobId)
    {
        await _repository.UpdateJobStatusAsync(jobId, JobStatus.Queued);
        QueueUpdated?.Invoke(this, EventArgs.Empty);

        if (_isRunning)
        {
            _ = TryLaunchNextJobsAsync();
        }
    }

    public async Task PauseJobAsync(Guid jobId)
    {
        await _engine.PauseAsync(jobId);
        _runningTasks.TryRemove(jobId, out _);
        QueueUpdated?.Invoke(this, EventArgs.Empty);

        if (_isRunning)
        {
            _ = TryLaunchNextJobsAsync();
        }
    }

    public async Task ResumeJobAsync(Guid jobId)
    {
        if (!_isRunning)
        {
            await StartQueueAsync();
        }
        await EnqueueJobAsync(jobId);
    }

    public async Task CancelJobAsync(Guid jobId)
    {
        await _engine.CancelAsync(jobId);
        _runningTasks.TryRemove(jobId, out _);
        QueueUpdated?.Invoke(this, EventArgs.Empty);

        if (_isRunning)
        {
            _ = TryLaunchNextJobsAsync();
        }
    }

    public async Task RemoveJobAsync(Guid jobId, bool deleteFiles)
    {
        _runningTasks.TryRemove(jobId, out _);
        await _engine.RemoveAsync(jobId, deleteFiles);
        QueueUpdated?.Invoke(this, EventArgs.Empty);

        if (_isRunning)
        {
            _ = TryLaunchNextJobsAsync();
        }
    }

    private async Task ProcessQueueLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            try
            {
                await TryLaunchNextJobsAsync(ct);
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue queue loop on transient error
            }
        }
    }

    private async Task TryLaunchNextJobsAsync(CancellationToken ct = default)
    {
        if (!_isRunning || ct.IsCancellationRequested) return;

        int availableSlots = _maxConcurrency - _runningTasks.Count;
        if (availableSlots <= 0) return;

        var allJobs = await _repository.GetAllAsync();
        var candidates = allJobs
            .Where(j => j.Status == JobStatus.Queued)
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.CreatedAt)
            .Take(availableSlots)
            .ToList();

        foreach (var job in candidates)
        {
            if (_runningTasks.ContainsKey(job.Id)) continue;

            var task = Task.Run(async () =>
            {
                try
                {
                    await _engine.StartAsync(job.Id, ct);
                }
                finally
                {
                    _runningTasks.TryRemove(job.Id, out _);
                    QueueUpdated?.Invoke(this, EventArgs.Empty);
                    if (_isRunning)
                    {
                        _ = TryLaunchNextJobsAsync();
                    }
                }
            }, ct);

            _runningTasks[job.Id] = task;
            QueueUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnEngineStateChanged(object? sender, JobStateChangedEventArgs e)
    {
        if (e.NewStatus is JobStatus.Completed or JobStatus.Failed or JobStatus.Paused or JobStatus.Canceled)
        {
            _runningTasks.TryRemove(e.JobId, out _);
            QueueUpdated?.Invoke(this, EventArgs.Empty);

            if (_isRunning)
            {
                _ = TryLaunchNextJobsAsync();
            }
        }
    }
}
