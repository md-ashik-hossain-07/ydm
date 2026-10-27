# YDM Automated Test & Quality Verification Report

**Document Version:** 1.0.0  
**Project:** YDM (Your Download Manager)  
**Execution Date:** October 2026  
**Environment:**
- **Operating System:** Microsoft Windows 11 Pro 64-bit (Build 26300)
- **Runtime:** .NET 8.0.427 (LTS) x64
- **Test Framework:** xUnit 2.9.2 + Microsoft.NET.Test.Sdk 17.11.1
- **Mock Infrastructure:** In-process `MockHttpServer` with dynamic byte range serving and simulated latency.

---

## 1. Test Summary

| Metric | Result |
|:---|:---|
| **Total Test Cases** | **37** |
| **Passed** | **37** |
| **Failed** | **0** |
| **Skipped** | **0** |
| **Duration** | **~864 ms** |
| **Build Status** | **Succeeded (0 Warnings, 0 Errors)** |

---

## 2. Test Execution Ledger

### 2.1 Security & Path Sanitization Tests (`PathSanitizerTests`)

| Test ID | Test Name / Scenario | Status | Verified Behavior |
|:---|:---|:---|:---|
| **TEST-01** | `SanitizeFileName_CleansUnsafeNames("../../etc/passwd")` | **Passed** | Traversal stripped; safely produces `passwd`. |
| **TEST-02** | `SanitizeFileName_CleansUnsafeNames("..\\..\\Windows\\System32\\cmd.exe")` | **Passed** | Directory path stripped; outputs `cmd.exe`. |
| **TEST-03** | `SanitizeFileName_CleansUnsafeNames("folder/subfolder/archive.zip")` | **Passed** | Subfolders stripped; outputs `archive.zip`. |
| **TEST-04** | `SanitizeFileName_CleansUnsafeNames("test<file>:name*?.txt")` | **Passed** | Illegal Windows characters replaced with `_`. |
| **TEST-05** | `SanitizeFileName_CleansUnsafeNames("CON.txt")` | **Passed** | Reserved DOS device name prepended with `_CON.txt`. |
| **TEST-06** | `SanitizeFileName_CleansUnsafeNames("NUL")` | **Passed** | DOS device name sanitized to `_NUL`. |
| **TEST-07** | `SanitizeFileName_CleansUnsafeNames("com1.dat")` | **Passed** | Sanitized to `_com1.dat`. |
| **TEST-08** | `SanitizeFileName_CleansUnsafeNames("aux.tar.gz")` | **Passed** | Root device name detected; sanitized to `_aux.tar.gz`. |
| **TEST-09** | `SanitizeFileName_CleansUnsafeNames("")` | **Passed** | Empty string falls back to `download.dat`. |
| **TEST-10** | `SanitizeFileName_CleansUnsafeNames("   ")` | **Passed** | Whitespace string falls back to `download.dat`. |
| **TEST-11** | `SanitizeFileName_CleansUnsafeNames("...")` | **Passed** | Dot-only string safely defaults to `download.dat`. |
| **TEST-12** | `GetSafeDestinationPath_PreventsPathTraversal` | **Passed** | Asserts canonical destination remains within target base directory. |
| **TEST-13** | `GetSafeDestinationPath_HandlesCollisions` | **Passed** | Appends ` (1).dat` and ` (2).dat` when filename already exists on disk. |

### 2.2 Network Security & SSRF Protection Tests (`UrlSecurityValidatorTests`)

| Test ID | Target URL Scenario | Status | Verified Behavior |
|:---|:---|:---|:---|
| **TEST-14** | `http://example.com/file.zip` | **Passed** | Valid public HTTP URL allowed. |
| **TEST-15** | `https://releases.ubuntu.com/jammy/ubuntu.iso` | **Passed** | Valid public HTTPS URL allowed. |
| **TEST-16** | `ftp://ftp.example.com/file.zip` | **Passed** | Non-HTTP/HTTPS protocol rejected. |
| **TEST-17** | `file:///C:/Windows/System32/cmd.exe` | **Passed** | Local filesystem URL rejected. |
| **TEST-18** | `javascript:alert(1)` | **Passed** | Arbitrary script protocol rejected. |
| **TEST-19** | `http://localhost:8080/secret` | **Passed** | Localhost probing blocked by default. |
| **TEST-20** | `http://127.0.0.1/admin` | **Passed** | Loopback IP address blocked. |
| **TEST-21** | `http://169.254.169.254/latest/meta-data/` | **Passed** | Cloud link-local metadata address blocked. |
| **TEST-22** | `http://10.0.0.1/internal` | **Passed** | RFC 1918 Class A private IP blocked. |
| **TEST-23** | `http://192.168.1.1/router` | **Passed** | RFC 1918 Class C private IP blocked. |
| **TEST-24** | `http://172.16.0.1/intranet` | **Passed** | RFC 1918 Class B private IP blocked. |
| **TEST-25** | `AllowLocalWhenFlagExplicitlyEnabled` | **Passed** | Local loopback permitted when user explicitly opts in. |

### 2.3 Storage & Persistence Tests (`StorageRepositoryTests`)

| Test ID | Scenario | Status | Verified Behavior |
|:---|:---|:---|:---|
| **TEST-26** | `SqliteRepository_PersistsAndRecoversStateAcrossRestarts` | **Passed** | SQLite WAL mode migrations succeed; jobs and segment states survive process restart; delete cascades properly. |
| **TEST-27** | `SettingsRepository_SavesAndLoadsConfiguration` | **Passed** | Application configuration persists in SQLite `Settings` table across restarts. |

### 2.4 Dynamic Segmentation Tests (`DynamicSegmentSplitterTests`)

| Test ID | Scenario | Status | Verified Behavior |
|:---|:---|:---|:---|
| **TEST-28** | `CreateInitialSegments_ContiguousRangesWithoutGaps` | **Passed** | Divides total byte count into contiguous slices with zero gap or overlap. |
| **TEST-29** | `CreateInitialSegments_SingleStreamWhenNotResumable` | **Passed** | Non-resumable download safely degrades to exactly 1 stream. |
| **TEST-30** | `TrySplitSegment_SplitsLargestRemainingChunkAtMidpoint` | **Passed** | Identifies largest lagging segment, splits at exact midpoint without byte loss. |

### 2.5 HTTP Engine Integration & Recovery Tests (`DownloadEngineIntegrationTests`)

| Test ID | Scenario | Status | Verified Behavior |
|:---|:---|:---|:---|
| **TEST-31** | `HttpProbeService_DetectsRangeAndRedirect` | **Passed** | Follows 302 redirects, captures intermediate `Content-Disposition`, validates range support. |
| **TEST-32** | `DownloadEngine_ExecutesMultiSegmentDownload_AndVerifiesIntegrity` | **Passed** | Downloads 512 KB with 4 connections; assembles file; SHA-256 matches remote source. |
| **TEST-33** | `DownloadEngine_ExecutesSingleStreamDownload_WhenNoRangeSupport` | **Passed** | Server with `Accept-Ranges: none` downloads to completion with single connection; SHA-256 matches. |
| **TEST-34** | `DownloadEngine_PausesAndResumes_WithoutCorruption` | **Passed** | Pauses mid-download; resumes remaining bytes; assembled file passes SHA-256 check. |

### 2.6 Edge Cases & Native Messaging (`AdditionalEdgeCaseTests`)

| Test ID | Scenario | Status | Verified Behavior |
|:---|:---|:---|:---|
| **TEST-35** | `DownloadEngine_DetectsChangedETagOnResume` | **Passed** | Server alters ETag mid-pause; engine detects change, invalidates stale chunks, restarts from 0. |
| **TEST-36** | `DownloadEngine_Cancellation_DoesNotLeaveCorruptedFile` | **Passed** | Canceling mid-stream cleans up; final target file is not left in a corrupted state. |
| **TEST-37** | `NativeMessageHost_RejectsInvalidPayloadsAndLimitsSize` | **Passed** | Verifies 32-bit unsigned LE length framing; enforces 1 MB max payload constraint. |

---

## 3. Test Command Reproduction

To execute the test suite independently:

```powershell
dotnet test tests/YDM.Tests/YDM.Tests.csproj --configuration Release
```
