namespace YDM.Core;

public enum JobStatus
{
    Queued,
    Probing,
    Connecting,
    Downloading,
    Paused,
    Assembling,
    Completed,
    Failed,
    Canceled
}

public enum SegmentStatus
{
    Pending,
    Downloading,
    Completed,
    Failed,
    Canceled
}

public enum DownloadPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

public enum DownloadCategory
{
    All,
    Compressed,
    Documents,
    Music,
    Programs,
    Video,
    Other
}
