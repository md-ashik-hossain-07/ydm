using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;

namespace YDM.Core.Security;

public record DestinationValidationResult(bool IsValid, string? ErrorMessage = null);

public enum DuplicateResolution
{
    AutoRename,
    Overwrite,
    Prompt
}

public static class PathSanitizer
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string SanitizeFileName(string rawFileName)
    {
        if (string.IsNullOrWhiteSpace(rawFileName))
            return "download.dat";

        // Strip any directory traversal and separators
        string fileName = Path.GetFileName(rawFileName.Trim());

        // Replace illegal Windows characters: < > : " / \ | ? * and control chars
        char[] invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(fileName.Length);
        foreach (char c in fileName)
        {
            if (c < 32 || Array.IndexOf(invalidChars, c) >= 0)
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }
        fileName = sb.ToString().Trim(' ', '.');

        if (string.IsNullOrWhiteSpace(fileName))
            return "download.dat";

        // Check against reserved DOS device names (e.g. "CON", "aux.tar.gz")
        string rootName = fileName.Split('.')[0];
        if (ReservedDeviceNames.Contains(rootName))
        {
            fileName = "_" + fileName;
        }

        // Limit filename length to 240 characters (NTFS safety)
        if (fileName.Length > 240)
        {
            string ext = Path.GetExtension(fileName);
            if (ext.Length > 20) ext = string.Empty;
            int maxBaseLength = Math.Max(1, 240 - ext.Length);
            fileName = fileName.Substring(0, maxBaseLength) + ext;
        }

        return string.IsNullOrWhiteSpace(fileName) ? "download.dat" : fileName;
    }

    public static string GetSafeDestinationPath(string targetDirectory, string rawFileName)
    {
        return ResolveDestinationPath(targetDirectory, rawFileName, DuplicateResolution.AutoRename);
    }

    public static string ResolveDestinationPath(string targetDirectory, string rawFileName, DuplicateResolution resolution = DuplicateResolution.AutoRename)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            targetDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                "Downloads");
        }

        string sanitizedName = SanitizeFileName(rawFileName);
        string fullTargetDir = Path.GetFullPath(targetDirectory);
        string fullDestination = Path.GetFullPath(Path.Combine(fullTargetDir, sanitizedName));

        // Security check: Must reside within targetDirectory
        if (!fullDestination.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException("Potential path traversal detected in destination path.");
        }

        if (!File.Exists(fullDestination) || resolution == DuplicateResolution.Overwrite)
        {
            return fullDestination;
        }

        // Handle collision by appending (1), (2), etc.
        string baseName = Path.GetFileNameWithoutExtension(sanitizedName);
        string extension = Path.GetExtension(sanitizedName);

        for (int i = 1; i < 10000; i++)
        {
            string candidate = Path.Combine(fullTargetDir, $"{baseName} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(fullTargetDir, $"{baseName}_{Guid.NewGuid():N}{extension}");
    }

    public static DestinationValidationResult ValidateDestinationDirectory(string targetDirectory, long? requiredBytes = null)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return new DestinationValidationResult(false, "Download folder path cannot be empty.");
        }

        try
        {
            string fullPath = Path.GetFullPath(targetDirectory);
            string? root = Path.GetPathRoot(fullPath);

            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    return new DestinationValidationResult(false, $"Selected drive '{root}' is not accessible or is disconnected. Please choose a valid storage location.");
                }

                if (requiredBytes.HasValue && requiredBytes.Value > 0)
                {
                    if (drive.AvailableFreeSpace < requiredBytes.Value)
                    {
                        double reqMb = requiredBytes.Value / (1024.0 * 1024.0);
                        double availMb = drive.AvailableFreeSpace / (1024.0 * 1024.0);
                        return new DestinationValidationResult(false, $"Insufficient disk space on drive '{root}'. Required: {reqMb:F1} MB, Available: {availMb:F1} MB.");
                    }
                }
            }

            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            // Test write permission with transient test file
            string testFile = Path.Combine(fullPath, $".ydm_write_test_{Guid.NewGuid():N}.tmp");
            using (var fs = File.Create(testFile, 1, FileOptions.DeleteOnClose))
            {
                fs.WriteByte(0);
            }

            return new DestinationValidationResult(true);
        }
        catch (UnauthorizedAccessException)
        {
            return new DestinationValidationResult(false, $"Access denied. You do not have write permission for directory '{targetDirectory}'.");
        }
        catch (Exception ex)
        {
            return new DestinationValidationResult(false, $"Invalid or inaccessible download location: {ex.Message}");
        }
    }

    public static string GetTempDirectory(Guid jobId)
    {
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YDM",
            "Temp",
            jobId.ToString("N"));

        if (!Directory.Exists(baseDir))
        {
            Directory.CreateDirectory(baseDir);
        }

        return baseDir;
    }
}
