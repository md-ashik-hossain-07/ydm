# Security Review & Threat Model (STRIDE)

**Project:** NovaFetch (Original Windows Download Manager)  
**Document:** Security Threat Model & Vulnerability Mitigation Analysis  
**Framework:** STRIDE (Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege)  

---

## 1. Threat Matrix & Mitigation Catalog

| Threat Category | STRIDE Type | Threat Scenario | Severity | NovaFetch Mitigation Architecture | Residual Risk |
|:---|:---|:---|:---|:---|:---|
| **Malicious Filenames & Path Traversal** | Tampering / Elevation of Privilege | An attacker hosts a file with `Content-Disposition: attachment; filename="../../Windows/System32/malicious.dll"` to overwrite critical system files. | **Critical** | Strict path sanitization: strip directory separators (`/`, `\`), replace reserved Windows characters (`< > : " / \ \| ? *`), and strip reserved DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`). Enforce that target destination strictly resolves within the user-approved folder via `Path.GetFullPath`. | Low (Standard Windows user permissions apply). |
| **Redirect-Based SSRF / Internal Network Probing** | Information Disclosure | A public URL redirects to internal private addresses (e.g. `http://127.0.0.1:8080/admin`, `http://169.254.169.254/metadata`) to probe local network services. | **High** | Pre-flight IP validation in custom `SocketsHttpHandler.ConnectCallback`. Reject IP addresses resolving to loopback (`127.0.0.0/8`, `::1`), link-local (`169.254.0.0/16`), or RFC 1918 private subnets unless explicitly whitelisted by the user in Settings. | Low. |
| **DNS Rebinding Attacks** | Tampering | A malicious domain initially resolves to a public IP to pass validation, then instantly switches DNS records to `127.0.0.1` for subsequent segment connections. | **Medium** | Re-validate resolved IP address on every individual socket connection before initiating TLS or HTTP handshakes; bind segment connections to the initial validated IP address. | Negligible. |
| **Native Messaging Abuse / Forged Messages** | Spoofing / Tampering | A malicious third-party Chrome extension or local process attempts to invoke `NovaFetch.Host.exe` to trigger arbitrary downloads. | **High** | The native host manifest strictly enforces `allowed_origins` matching only the official NovaFetch Extension ID. On Windows, Chromium verifies the calling extension origin before launching the host process. | Low (Relies on browser sandbox origin enforcement). |
| **Unsafe Process Execution (Auto-Run)** | Elevation of Privilege | An downloaded executable (`.exe`, `.bat`, `.cmd`) is automatically executed upon completion, leading to malware infection. | **High** | NovaFetch **never** automatically executes downloaded files by default. User must explicitly click "Open". When opening executables, NovaFetch attaches the Windows Zone Identifier alternate data stream (`Zone.Identifier: ZoneId=3` / Internet) so Windows SmartScreen and Defender inspect the file. | Medium (User may still choose to run unknown binary). |
| **Disk Exhaustion / Zip Bombs** | Denial of Service | An attacker provides an infinite streaming HTTP response without `Content-Length` or a 100 GB file that exhausts all available drive space. | **High** | Check free disk space via `DriveInfo.AvailableFreeSpace` before allocating files. For streaming downloads with unknown length, enforce a configurable hard maximum ceiling (default: 10 GB) and abort if exceeded. | Negligible. |
| **Insecure Temporary File Permissions** | Information Disclosure | Other standard users on a shared Windows workstation read partial download segments containing sensitive documents or tokens in `%TEMP%`. | **Medium** | Temporary segment directories are created inside `%LOCALAPPDATA%\NovaFetch\Temp` with strict NTFS Discretionary Access Control Lists (DACLs) granting read/write access solely to the current user's SID (`CurrentUserOnly`). | Negligible on NTFS filesystems. |
| **Credential & Log Leakage** | Information Disclosure | Diagnostic logs record sensitive HTTP request headers containing `Authorization: Bearer ...` or `Cookie: session_id=...`. | **Medium** | Structured logging (Serilog) employs regex redacting sinks that automatically scrub `Authorization`, `Cookie`, `Set-Cookie`, and basic auth tokens before writing to disk. | Low. |

---

## 2. Path Sanitization Algorithm

```csharp
namespace NovaFetch.Core.Security;

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

        // Strip path traversal directory separators
        string fileName = Path.GetFileName(rawFileName);

        // Replace illegal Windows characters: < > : " / \ | ? *
        char[] invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(fileName.Length);
        foreach (char c in fileName)
        {
            sb.Append(invalidChars.Contains(c) ? '_' : c);
        }
        fileName = sb.ToString().Trim(' ', '.');

        // Check against reserved DOS device names
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (ReservedDeviceNames.Contains(nameWithoutExtension))
        {
            fileName = "_" + fileName;
        }

        // Limit filename length to 255 characters (NTFS maximum)
        if (fileName.Length > 240)
        {
            string ext = Path.GetExtension(fileName);
            fileName = fileName.Substring(0, 240 - ext.Length) + ext;
        }

        return string.IsNullOrWhiteSpace(fileName) ? "download.dat" : fileName;
    }

    public static string SecureCombine(string baseDirectory, string sanitizedFileName)
    {
        string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, sanitizedFileName));
        string fullBaseDir = Path.GetFullPath(baseDirectory);

        // Enforce that target destination strictly resides within baseDirectory
        if (!fullPath.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException("Path traversal attempt detected!");
        }

        return fullPath;
    }
}
```

---

## 3. Windows SmartScreen Zone Identifier Attachment

To ensure Windows Defender and SmartScreen properly protect the user against untrusted web downloads, NovaFetch writes an NTFS Alternate Data Stream (ADS) upon file completion:

```csharp
namespace NovaFetch.Core.Security;

public static class SecurityZoneIdentifier
{
    // Write Zone.Identifier: ZoneId=3 (URLZONE_INTERNET)
    public static void AttachInternetZone(string filePath, string sourceUrl, string referrerUrl)
    {
        try
        {
            string zonePath = filePath + ":Zone.Identifier";
            string content = $"[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl={referrerUrl}\r\nHostUrl={sourceUrl}\r\n";
            File.WriteAllText(zonePath, content);
        }
        catch (Exception ex)
        {
            // Non-NTFS drives (FAT32, exFAT) do not support Alternate Data Streams
            Log.Debug("Alternate data streams unsupported on destination volume: {Message}", ex.Message);
        }
    }
}
```
