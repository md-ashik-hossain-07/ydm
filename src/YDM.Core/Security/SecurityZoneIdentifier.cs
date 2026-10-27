using System;
using System.IO;

namespace YDM.Core.Security;

public static class SecurityZoneIdentifier
{
    /// <summary>
    /// Attaches the Windows SmartScreen Zone Identifier (ZoneId=3: Internet) to downloaded files on NTFS.
    /// Fails gracefully on non-NTFS volumes without throwing.
    /// </summary>
    public static void AttachInternetZone(string filePath, string sourceUrl, string? referrerUrl = null)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        try
        {
            string zoneStreamPath = $"{filePath}:Zone.Identifier";
            string content = $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl={sourceUrl}\r\n";
            if (!string.IsNullOrWhiteSpace(referrerUrl))
            {
                content += $"ReferrerUrl={referrerUrl}\r\n";
            }
            File.WriteAllText(zoneStreamPath, content);
        }
        catch
        {
            // Silently ignore if filesystem does not support alternate data streams (FAT32, exFAT, network shares)
        }
    }
}
