using System;
using System.IO;

namespace YDM.Core.Models;

public class ApplicationSettings
{
    public string DefaultDownloadDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
        "Downloads");
        
    public int MaxConcurrentDownloads { get; set; } = 2;
    public int DefaultConnectionsPerDownload { get; set; } = 8;
    public bool AutoStartDownloads { get; set; } = true;
    public bool EnableBrowserInterception { get; set; } = true;
    public int SpeedLimitBytesPerSecond { get; set; } = 0; // 0 = unlimited
    public int NetworkTimeoutSeconds { get; set; } = 30;
    public bool AllowLocalIpAddresses { get; set; } = false; // For testing and LAN
}
