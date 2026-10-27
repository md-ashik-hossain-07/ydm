using System;

namespace YDM.Core.Models;

public class ProbeResult
{
    public string FinalUrl { get; set; } = string.Empty;
    public long? ContentLength { get; set; }
    public bool SupportsRange { get; set; }
    public string? SuggestedFileName { get; set; }
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public int StatusCode { get; set; }
    public string? ContentType { get; set; }
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}
