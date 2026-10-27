using System;

namespace YDM.Core.Models;

public class DownloadSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public int Index { get; set; }
    
    public long StartOffset { get; set; }
    public long EndOffset { get; set; }
    public long CurrentOffset { get; set; }
    
    public SegmentStatus Status { get; set; } = SegmentStatus.Pending;
    public string TempFilePath { get; set; } = string.Empty;
    
    public long TotalBytes => EndOffset >= StartOffset ? (EndOffset - StartOffset + 1) : 0;
    public long DownloadedBytes => CurrentOffset >= StartOffset ? (CurrentOffset - StartOffset) : 0;
    public double ProgressPercentage => TotalBytes > 0 ? Math.Clamp((double)DownloadedBytes / TotalBytes * 100.0, 0.0, 100.0) : 0.0;
    public bool IsComplete => CurrentOffset > EndOffset || (TotalBytes > 0 && DownloadedBytes >= TotalBytes);
}
