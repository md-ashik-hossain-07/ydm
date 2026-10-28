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
    
    // UI & Appearance
    public string Theme { get; set; } = "System"; // "System", "Light", "Dark"
    
    // Quality-of-Life & Notifications
    public bool EnableClipboardMonitoring { get; set; } = false;
    public bool EnableNotifications { get; set; } = true;
    
    // Browser Interception Mode
    public string InterceptionMode { get; set; } = "Automatic"; // "Automatic", "Ask", "Manual"
    
    // Queue Scheduler
    public bool QueueScheduleEnabled { get; set; } = false;
    public string? QueueScheduleStartTime { get; set; } = null; // "HH:mm" 24-hr format
    public string? QueueScheduleStopTime { get; set; } = null;  // "HH:mm" 24-hr format
    
    // File Management
    public bool ConfirmFileDeletion { get; set; } = true;
    public string DuplicateFileAction { get; set; } = "Prompt"; // "Prompt", "AutoRename", "Overwrite"
    
    // UI View State
    public string ViewMode { get; set; } = "Grid"; // "Grid", "List"
    public bool ShowDetailsPanel { get; set; } = true;
}
