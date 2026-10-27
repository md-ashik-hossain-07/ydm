using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using YDM.Core;
using YDM.Core.Models;

namespace YDM.UI.ViewModels;

public partial class DownloadItemViewModel : ObservableObject
{
    public DownloadJob Job { get; }

    public Guid Id => Job.Id;
    public string Url => Job.Url;

    [ObservableProperty]
    private string _fileName;

    [ObservableProperty]
    private JobStatus _status;

    [ObservableProperty]
    private long _downloadedBytes;

    [ObservableProperty]
    private long? _totalBytes;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private long _speedBytesPerSecond;

    [ObservableProperty]
    private TimeSpan? _remainingTime;

    [ObservableProperty]
    private string? _errorMessage;

    public DownloadCategory Category => Job.Category;
    public DateTime CreatedAt => Job.CreatedAt;
    public string DestinationPath => Job.DestinationPath;

    public ObservableCollection<DownloadSegment> Segments { get; } = new();

    public DownloadItemViewModel(DownloadJob job)
    {
        Job = job;
        _fileName = job.FileName;
        _status = job.Status;
        _downloadedBytes = job.DownloadedBytes;
        _totalBytes = job.TotalBytes;
        _progressPercentage = job.ProgressPercentage;
        _speedBytesPerSecond = job.SpeedBytesPerSecond;
        _remainingTime = job.RemainingTime;
        _errorMessage = job.ErrorMessage;

        foreach (var seg in job.Segments)
        {
            Segments.Add(seg);
        }
    }

    public void UpdateProgress(long downloadedBytes, long? totalBytes, long speed, TimeSpan? remaining)
    {
        DownloadedBytes = downloadedBytes;
        TotalBytes = totalBytes;
        SpeedBytesPerSecond = speed;
        RemainingTime = remaining;

        if (totalBytes.HasValue && totalBytes.Value > 0)
        {
            ProgressPercentage = Math.Clamp((double)downloadedBytes / totalBytes.Value * 100.0, 0.0, 100.0);
        }
        else if (Status == JobStatus.Completed)
        {
            ProgressPercentage = 100.0;
        }

        OnPropertyChanged(nameof(SizeFormatted));
        OnPropertyChanged(nameof(SpeedFormatted));
        OnPropertyChanged(nameof(RemainingTimeFormatted));
    }

    public void UpdateStatus(JobStatus newStatus, string? errorMessage = null)
    {
        Status = newStatus;
        ErrorMessage = errorMessage;
        if (newStatus == JobStatus.Completed)
        {
            ProgressPercentage = 100.0;
            SpeedBytesPerSecond = 0;
            RemainingTime = TimeSpan.Zero;
        }
        OnPropertyChanged(nameof(StatusFormatted));
    }

    public string SizeFormatted
    {
        get
        {
            if (TotalBytes.HasValue && TotalBytes.Value > 0)
            {
                return $"{FormatBytes(DownloadedBytes)} / {FormatBytes(TotalBytes.Value)}";
            }
            return FormatBytes(DownloadedBytes);
        }
    }

    public string SpeedFormatted => SpeedBytesPerSecond > 0 ? $"{FormatBytes(SpeedBytesPerSecond)}/s" : "—";

    public string RemainingTimeFormatted
    {
        get
        {
            if (Status == JobStatus.Completed) return "Done";
            if (!RemainingTime.HasValue || RemainingTime.Value <= TimeSpan.Zero) return "—";
            var t = RemainingTime.Value;
            if (t.TotalHours >= 1)
                return $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1)
                return $"{t.Minutes}m {t.Seconds}s";
            return $"{t.Seconds}s";
        }
    }

    public string StatusFormatted => Status switch
    {
        JobStatus.Queued => "Queued",
        JobStatus.Probing => "Connecting...",
        JobStatus.Connecting => "Connecting...",
        JobStatus.Downloading => "Downloading",
        JobStatus.Paused => "Paused",
        JobStatus.Assembling => "Assembling...",
        JobStatus.Completed => "Completed",
        JobStatus.Failed => "Failed",
        JobStatus.Canceled => "Canceled",
        _ => Status.ToString()
    };

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F1} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }
}
