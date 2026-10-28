using System;
using System.Collections.Generic;

namespace YDM.Core.Models;

public class DownloadJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = string.Empty;
    public string? Referrer { get; set; }
    public string? Cookies { get; set; }
    public string? UserAgent { get; set; }
    
    public string FileName { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string TempDirectory { get; set; } = string.Empty;
    
    public long? TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public bool IsResumable { get; set; }
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public DownloadPriority Priority { get; set; } = DownloadPriority.Normal;
    public int MaxConnections { get; set; } = 8;
    public List<DownloadSegment> Segments { get; set; } = new();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? ErrorMessage { get; set; }
    
    public long SpeedBytesPerSecond { get; set; }
    public TimeSpan? RemainingTime { get; set; }

    public double ProgressPercentage
    {
        get
        {
            if (TotalBytes.HasValue && TotalBytes.Value > 0)
            {
                return Math.Clamp((double)DownloadedBytes / TotalBytes.Value * 100.0, 0.0, 100.0);
            }
            return Status == JobStatus.Completed ? 100.0 : 0.0;
        }
    }

    public DownloadCategory Category
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FileName)) return DownloadCategory.Other;
            string ext = System.IO.Path.GetExtension(FileName).ToLowerInvariant();
            return ext switch
            {
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" or ".iso" => DownloadCategory.Compressed,
                ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".epub" => DownloadCategory.Documents,
                ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma" => DownloadCategory.Music,
                ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" or ".apk" or ".appx" => DownloadCategory.Programs,
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".wmv" or ".flv" => DownloadCategory.Video,
                _ => DownloadCategory.Other
            };
        }
    }
}
