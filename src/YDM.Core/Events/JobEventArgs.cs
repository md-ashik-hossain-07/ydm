using System;

namespace YDM.Core.Events;

public class JobProgressEventArgs : EventArgs
{
    public Guid JobId { get; }
    public long DownloadedBytes { get; }
    public long? TotalBytes { get; }
    public long SpeedBytesPerSecond { get; }
    public TimeSpan? RemainingTime { get; }

    public JobProgressEventArgs(Guid jobId, long downloadedBytes, long? totalBytes, long speed, TimeSpan? remainingTime)
    {
        JobId = jobId;
        DownloadedBytes = downloadedBytes;
        TotalBytes = totalBytes;
        SpeedBytesPerSecond = speed;
        RemainingTime = remainingTime;
    }
}

public class JobStateChangedEventArgs : EventArgs
{
    public Guid JobId { get; }
    public JobStatus OldStatus { get; }
    public JobStatus NewStatus { get; }
    public string? ErrorMessage { get; }

    public JobStateChangedEventArgs(Guid jobId, JobStatus oldStatus, JobStatus newStatus, string? errorMessage = null)
    {
        JobId = jobId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        ErrorMessage = errorMessage;
    }
}
