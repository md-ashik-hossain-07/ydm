using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    [ObservableProperty]
    private ImageSource? _thumbnail;

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

        TryLoadLocalThumbnail();
    }

    public void TryLoadLocalThumbnail()
    {
        if (Thumbnail != null || string.IsNullOrWhiteSpace(DestinationPath) || !File.Exists(DestinationPath))
            return;

        try
        {
            string ext = Path.GetExtension(DestinationPath).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp")
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(DestinationPath);
                bmp.DecodePixelWidth = 120;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                Thumbnail = bmp;
            }
        }
        catch { }
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
        OnPropertyChanged(nameof(FormattedTotalSize));
        OnPropertyChanged(nameof(SizeAndCategoryFormatted));
        OnPropertyChanged(nameof(StatusDetailsFormatted));
        OnPropertyChanged(nameof(FileSizeBytesFormatted));
        OnPropertyChanged(nameof(DownloadedBytesFormatted));
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
            TryLoadLocalThumbnail();
        }

        OnPropertyChanged(nameof(StatusFormatted));
        OnPropertyChanged(nameof(StatusDetailsFormatted));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsPausedOrQueued));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(CompletedDateFormatted));
    }

    public bool IsCompleted => Status == JobStatus.Completed;
    public bool IsDownloading => Status == JobStatus.Downloading || Status == JobStatus.Connecting || Status == JobStatus.Probing || Status == JobStatus.Assembling;
    public bool IsPausedOrQueued => Status == JobStatus.Paused || Status == JobStatus.Queued;
    public bool IsFailed => Status == JobStatus.Failed;

    public string CategoryDisplayName => Category switch
    {
        DownloadCategory.Documents => "Documents",
        DownloadCategory.Compressed => "Compressed",
        DownloadCategory.Video => "Video",
        DownloadCategory.Music => "Music",
        DownloadCategory.Programs => "Programs",
        _ => "Others"
    };

    public string FileExtension
    {
        get
        {
            string ext = Path.GetExtension(FileName).ToUpperInvariant().TrimStart('.');
            return string.IsNullOrWhiteSpace(ext) ? "FILE" : ext;
        }
    }

    public string BadgeType
    {
        get
        {
            string ext = Path.GetExtension(FileName).ToLowerInvariant();
            if (ext == ".pdf") return "PDF";
            if (Category == DownloadCategory.Video) return "VIDEO";
            if (Category == DownloadCategory.Compressed) return "ZIP";
            if (Category == DownloadCategory.Programs) return "EXE";
            if (Category == DownloadCategory.Music) return "AUDIO";
            if (Category == DownloadCategory.Documents) return "DOC";
            return "OTHER";
        }
    }

    public string FormattedTotalSize
    {
        get
        {
            if (TotalBytes.HasValue && TotalBytes.Value > 0)
            {
                return FormatBytes(TotalBytes.Value);
            }
            if (Status == JobStatus.Completed && DownloadedBytes > 0)
            {
                return FormatBytes(DownloadedBytes);
            }
            return "Unknown size";
        }
    }

    public string SizeAndCategoryFormatted => $"{FormattedTotalSize} • {CategoryDisplayName}";

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
                return $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
            if (t.TotalMinutes >= 1)
                return $"{t.Minutes:D2}:{t.Seconds:D2}";
            return $"00:00:{t.Seconds:D2}";
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

    public string StatusDetailsFormatted
    {
        get
        {
            string dateStr = (Job.CompletedAt ?? CreatedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            return Status switch
            {
                JobStatus.Completed => $"Completed • {SizeFormatted} • {dateStr}",
                JobStatus.Downloading => $"{SizeFormatted} • {SpeedFormatted}" + (RemainingTime.HasValue && RemainingTime.Value > TimeSpan.Zero ? $" • {RemainingTimeFormatted} remaining" : ""),
                JobStatus.Connecting or JobStatus.Probing => $"Connecting... • {SpeedFormatted}",
                JobStatus.Assembling => "Assembling segments...",
                JobStatus.Queued => $"Queued • {CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}",
                JobStatus.Paused => $"Paused • {SizeFormatted}",
                JobStatus.Failed => $"Failed • {ErrorMessage ?? "Download interrupted"}",
                _ => $"{StatusFormatted} • {dateStr}"
            };
        }
    }

    // Right-Hand Details Panel Formatted Fields
    public string SaveLocationFormatted
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DestinationPath)) return "—";
            string? dir = Path.GetDirectoryName(DestinationPath);
            return string.IsNullOrWhiteSpace(dir) ? DestinationPath : dir;
        }
    }

    public string FileSizeBytesFormatted
    {
        get
        {
            long bytes = TotalBytes ?? DownloadedBytes;
            if (bytes > 0)
            {
                return $"{bytes:N0} bytes ({FormatBytes(bytes)})";
            }
            return "Unknown";
        }
    }

    public string DownloadedBytesFormatted => $"{DownloadedBytes:N0} bytes";

    public string DateAddedFormatted => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string CompletedDateFormatted => Job.CompletedAt.HasValue ? Job.CompletedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";

    public string ResumeSupportFormatted => Job.IsResumable ? "Yes" : "No";

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
