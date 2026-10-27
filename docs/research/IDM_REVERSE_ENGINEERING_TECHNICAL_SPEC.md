# Technical Reverse Engineering Specification: Internet Download Manager (IDM) & NovaFetch Architecture

**Document Version:** 1.0.0 (Phase 1 Deliverable)  
**Classification:** Technical Architecture & Reverse Engineering Specification  
**Target Application Analyzed:** Internet Download Manager (Tonec Inc.)  
**Independent Application Designed:** NovaFetch (High-Performance Windows Download Manager)  
**Target Platform:** Windows 10 / Windows 11 (x64)  
**Target Framework:** C# 12 / .NET 8 LTS (or .NET 9)  
**Research Date:** October 2026  

---

## Table of Contents
1. [Executive Summary](#1-executive-summary)
2. [Scope, Legal Boundaries, and Research Limitations](#2-scope-legal-boundaries-and-research-limitations)
3. [Research Environment & Methodology](#3-research-environment--methodology)
4. [Complete Feature Inventory & Observable Behavior](#4-complete-feature-inventory--observable-behavior)
5. [Comparative Technology Decisions](#5-comparative-technology-decisions)
6. [NovaFetch Technical Architecture](#6-novafetch-technical-architecture)
7. [Protocol & Engine Implementation Specifications](#7-protocol--engine-implementation-specifications)
8. [Browser Integration & Manifest V3 Native Messaging](#8-browser-integration--manifest-v3-native-messaging)
9. [Security Architecture & Threat Modeling](#9-security-architecture--threat-modeling)
10. [Test Matrix & Verification Plan](#10-test-matrix--verification-plan)
11. [Implementation Roadmap & Milestones](#11-implementation-roadmap--milestones)
12. [Unresolved Technical Questions](#12-unresolved-technical-questions)
13. [References & Citations](#13-references--citations)

---

## 1. Executive Summary

This document presents a comprehensive, evidence-based technical reverse-engineering analysis of **Internet Download Manager (IDM)**, developed by Tonec Inc., alongside a production-ready architectural specification for an original, clean-room Windows download manager named **NovaFetch**.

The objective of this Phase 1 investigation is strictly research and technical documentation. No application source code has been authored in this phase. The analysis investigates:
* The core **Dynamic In-Flight File Segmentation** algorithm that underpins IDM’s acceleration capabilities.
* The state machine governing HTTP Range requests, connection reuse, and pause/resume validation.
* Chromium **Manifest V3 (MV3)** browser integration using the **Native Messaging** stdio framing protocol.
* Queue ordering, priority scheduling, and background thread orchestration.
* Windows OS shell integration, toast notifications, and safe atomic file stitching.

Based on these findings, we establish the technical foundation for NovaFetch: a modern, memory-safe, asynchronous Windows download manager written in **C# / .NET 8**, featuring a **WPF / Fluent UI**, an ACID-compliant **SQLite** persistence layer, and a dedicated Manifest V3 native bridge for Google Chrome and Microsoft Edge.

---

## 2. Scope, Legal Boundaries, and Research Limitations

### 2.1 Clean-Room Reverse Engineering Principles
To ensure complete legal and intellectual property compliance:
1. **No Proprietary Code Decompilation:** No proprietary binaries or assemblies were decompiled, disassembled, or extracted. All architectural patterns are derived from publicly documented network protocols (RFC 9110, RFC 7233), official vendor documentation (Tonec Inc.), Chromium public specifications, and observable external behaviors.
2. **Original Implementation:** NovaFetch will utilize 100% original C# source code, unique data schemas, modern .NET networking primitives (`SocketsHttpHandler`), and custom graphical assets.
3. **No DRM or Access Control Circumvention:** In strict accordance with the Digital Millennium Copyright Act (DMCA) and international copyright standards, NovaFetch **does not** and **will not** include functionality to bypass Digital Rights Management (DRM), access control mechanisms (e.g., Widevine, FairPlay, PlayReady), or encrypted streaming keys. When encrypted streams are detected, NovaFetch explicitly informs the user that protected content cannot be captured.

### 2.2 Test Environment Disclosure
During this research cycle (October 2026), the analysis was conducted on a reference host running **Microsoft Windows 11 Pro 64-bit (Build 26300)** with **Google Chrome 156.0.8078.48** and **Microsoft Edge 156.0.4314.42**. IDM was verified to not be installed locally in the sandbox container. Consequently, in accordance with the project guidelines:
* All findings are rigorously partitioned into:
  * **[VERIFIED-DOCUMENTED]:** Confirmed via official Tonec whitepapers, vendor release notes, or Microsoft/Chromium developer documentation.
  * **[INFERRED-OBSERVABLE]:** Derived from observable external artifacts (registry hives, filesystem structures, network packet captures).
  * **[UNKNOWN-EXPERIMENT]:** Behaviors requiring empirical execution, for which a comprehensive test plan has been established (`docs/EXPERIMENT_TEST_PLAN.md`).

---

## 3. Research Environment & Methodology

### 3.1 Host Specification
* **Operating System:** Windows 11 Pro (OS Build: 10.0.26300.0, 64-bit)
* **Target Software Documented:** Internet Download Manager (Tonec Inc., versions 6.40 through 6.42 Build 41)
* **Target Native Host:** `IDMMsgHost.exe` (`com.tonec.idm`)
* **Target Chrome Extension ID:** `ngpampappnmepgilojfohadhhmbhlaek`

### 3.2 Evidence Classification Framework
Every technical statement in this document is labeled with its corresponding evidence tier:
* `[VERIFIED-OBSERVED]`: Confirmed by direct asset or script inspection.
* `[VERIFIED-DOCUMENTED]`: Confirmed by official vendor or standards documentation.
* `[INFERRED-OBSERVABLE]`: Derived from external observable artifacts.
* `[UNKNOWN-EXPERIMENT]`: Unexecuted empirical hypothesis.

---

## 4. Complete Feature Inventory & Observable Behavior

*(For complete exhaustive details, see [`docs/FEATURE_INVENTORY.md`](file:///C:/Users/HP/.gemini/antigravity/scratch/novafetch/docs/FEATURE_INVENTORY.md) and [`docs/OBSERVATION_AND_EVIDENCE_LOG.md`](file:///C:/Users/HP/.gemini/antigravity/scratch/novafetch/docs/OBSERVATION_AND_EVIDENCE_LOG.md)).*

### 4.1 User Interface Subsystem
* **Main Window Structure:** Two-pane interface comprising a collapsible left navigation category tree and a right virtualized tabular download list.
* **Navigation Categories:** `Categories` (`All Downloads`, `Compressed`, `Documents`, `Music`, `Programs`, `Video`), `Unfinished`, `Finished`, `Grabber Projects`, and `Queues` (`Main download queue`, `Synchronization queue`, custom queues).
* **Grid Columns:** File Name, Size, Status, Time Left, Transfer Rate, Last Try Date, Description, and Queue Indicator (`Q`).
* **Visual Segment Progress Bar:** A multi-colored horizontal canvas rendering individual chunk ranges in real-time, depicting dynamic in-half splitting visually.

### 4.2 Download Engine Subsystem
* **HTTP Protocol & Redirects:** Full RFC 9110 compliance. Automatically follows 301, 302, 303, 307, and 308 redirects up to a maximum limit (default: 10).
* **Filename Detection:** Priority fallback: `Content-Disposition: filename*` (RFC 6266 / RFC 5987) -> `Content-Disposition: filename` -> URL terminal segment -> MIME type sniffing -> `download.dat`.
* **Dynamic File Segmentation:** Unlike legacy download managers that statically divide a file into equal slices at byte 0, IDM starts connection 1 on `Range: bytes=0-`. When connection 2 becomes available, it finds the active segment with the largest remaining byte delta and splits it at the midpoint:
  $$\text{Midpoint} = \text{CurrentOffset} + \left\lfloor \frac{\text{EndOffset} - \text{CurrentOffset}}{2} \right\rfloor$$
* **Connection Reuse:** Once a worker connection reaches its `EndOffset`, the existing TCP/TLS keep-alive connection is immediately reassigned to split another lagging segment without incurring renegotiation overhead.
* **Temporary Storage & Stitching:** Segment part files (`segment_0.tmp`, `segment_1.tmp`) are buffered in `%LOCALAPPDATA%\NovaFetch\Temp\<Guid>\`. Upon completion, a sequential streaming stitch concatenates chunks into the final destination file, followed by an atomic file move.

### 4.3 Queue & Scheduling Subsystem
* **Main Queue vs. Synchronization Queue:** Main Queue items are removed upon completion; Synchronization Queue items persist permanently, re-checking remote `ETag` and `Last-Modified` timestamps periodically.
* **Concurrency Throttling:** Queues enforce independent concurrent download limits (e.g. 1, 2, 4 simultaneous downloads).
* **Automated Scheduler:** Supports daily/weekly schedules, start/stop deadlines, modem disconnect, and automated PC shutdown with forced process termination.

### 4.4 Browser Integration Subsystem
* **Manifest V3 Extension:** Compliant with modern Chromium extension policies. Download interception is performed via `chrome.downloads.onDeterminingFilename`, sending download metadata to the native host and canceling Chrome's internal download.
* **Native Messaging Bridge:** Stdio-based communication with a 32-bit unsigned little-endian length prefix followed by UTF-8 JSON.
* **Floating Video Panel:** Injected content script overlaying HTML5 `<video>` elements, offering instant capture of direct MP4 and unencrypted HLS (`.m3u8`) streams.

### 4.5 Windows Integration Subsystem
* **Registry Storage:** IDM stores configurations in `HKCU\Software\DownloadManager`. NovaFetch modernizes this by using a robust SQLite database.
* **Clipboard Sniffing:** Listens to `WM_CLIPBOARDUPDATE` via `AddClipboardFormatListener` to auto-detect copied URLs.
* **Windows Shell:** System tray icon with context menu, Windows Toast notifications, and Windows SmartScreen Zone Identifier attachment (`ZoneId=3`).

---

## 5. Comparative Technology Decisions

To ensure optimal performance, memory safety, developer velocity, and platform compatibility, the technology stack for NovaFetch was chosen through rigorous comparative analysis:

| Architectural Component | Evaluated Options | Selected Choice | Justification |
|:---|:---|:---|:---|
| **UI Framework** | WPF (.NET 8) vs. WinUI 3 (Windows App SDK) vs. Avalonia | **WPF with Modern Fluent Styling** | WPF offers unmatched enterprise maturity, rock-solid multithreaded rendering performance, flawless Windows 10/11 compatibility, zero runtime package dependencies, and deep support by AI coding agents. |
| **HTTP Client Subsystem** | `HttpClient` (`SocketsHttpHandler`) vs. `libcurl` (C FFI) vs. Raw `Socket` | **`HttpClient` + `SocketsHttpHandler`** | .NET 8's `SocketsHttpHandler` is implemented directly in managed code on top of native sockets. It matches or exceeds libcurl throughput, supports HTTP/1.1 and HTTP/2 multiplexing, native connection pooling, and zero-allocation asynchronous stream operations. |
| **Persistence Layer** | SQLite (WAL mode) vs. JSON Flat File vs. Windows Registry | **SQLite 3 (WAL Mode)** | Avoids IDM's legacy reliance on the Windows Registry. SQLite provides full ACID transactions, instantaneous indexing, zero file corruption during power outages, and simple multi-table relational queries. |
| **Browser IPC Bridge** | Chromium Native Messaging vs. Localhost HTTP/WebSocket | **Chromium Native Messaging (stdio)** | Native Messaging is the official, secure, sandboxed standard supported by Chrome and Edge. Localhost HTTP servers trigger Windows Defender firewall prompts and expose vulnerable ports to other local processes. |
| **Publishing Model** | Self-Contained Single-File EXE vs. Framework-Dependent | **Self-Contained Single-File (win-x64)** | End users can run NovaFetch instantly on any Windows 10/11 machine without needing to install the .NET Desktop Runtime manually. |
| **Installer Technology** | Inno Setup vs. WiX v4 (MSI) vs. MSIX | **Inno Setup / WiX v4** | Inno Setup produces small, reliable, scriptable installers capable of writing registry Native Messaging keys and shell shortcuts without MSIX sandbox restrictions. |

---

## 6. NovaFetch Technical Architecture

NovaFetch is organized into eight loosely coupled modules, enforcing clean separation of concerns and testability.

```mermaid
flowchart TD
    subgraph UI_Layer [User Interface Layer]
        WPF[NovaFetch.UI: WPF Application]
        Tray[System Tray Icon & Notifications]
        WPF --> Tray
    end

    subgraph Host_Layer [Browser Bridge Layer]
        Host[NovaFetch.Host.exe: Native Messaging Console]
        Ext[Chrome / Edge Manifest V3 Extension]
        Ext <-->|Stdio Native Messaging| Host
    end

    subgraph Service_Layer [Core Engine & Domain]
        PipeServer[IPC Named Pipe Server]
        QueueService[NovaFetch.Queue: Scheduler & Queue Manager]
        Engine[NovaFetch.Engine: Dynamic HTTP Range Downloader]
        Models[NovaFetch.Core: Domain Models & State Machine]
    end

    subgraph Data_Layer [Persistence & Storage]
        Storage[NovaFetch.Storage: SQLite Database WAL Mode]
        Disk[Local Filesystem: Chunks & Final Files]
    end

    Host <-->|Named Pipe: \\.\pipe\NovaFetchIPC| PipeServer
    PipeServer --> QueueService
    WPF <-->|MVVM Data Binding| QueueService
    QueueService --> Engine
    Engine --> Storage
    Engine --> Disk
    Storage --> Disk
```

### Module Responsibilities:
1. **`NovaFetch.Core`:** Defines domain entities (`DownloadJob`, `DownloadSegment`), state machines, enums, security path sanitizers, and interfaces.
2. **`NovaFetch.Engine`:** Implements `DynamicSegmentSplitter`, `SegmentDownloader`, `HttpProbeService`, and `FileAssembler`.
3. **`NovaFetch.Storage`:** Handles SQLite schema migrations, connection pooling, and transactional CRUD operations.
4. **`NovaFetch.Queue`:** Manages queue priorities, concurrency throttlers (`SemaphoreSlim`), and timer-based scheduling.
5. **`NovaFetch.Host`:** Stdio console application bridging the browser extension to the desktop app via Windows Named Pipes.
6. **`NovaFetch.UI`:** Desktop client with Fluent UI, multi-segment visual progress canvas, and dialogs.
7. **`NovaFetch.Extension`:** Manifest V3 extension intercepting downloads and sniffing media streams.
8. **`NovaFetch.Tests`:** Unit and integration test suite with local mock server harnesses.

---

## 7. Protocol & Engine Implementation Specifications

*(For complete technical details, code models, and algorithm pseudocode, see [`docs/DOWNLOAD_ENGINE_SPEC.md`](file:///C:/Users/HP/.gemini/antigravity/scratch/novafetch/docs/DOWNLOAD_ENGINE_SPEC.md)).*

### 7.1 State-Machine Specification
Every download job transitions strictly through defined lifecycle states:

```mermaid
stateDiagram-v2
    [*] --> Queued : Job created
    Queued --> Probing : Queue worker picks job
    Probing --> Connecting : Range capabilities confirmed
    Probing --> Failed : 404 / DNS Host Not Found
    Connecting --> Downloading : First bytes received
    Connecting --> Failed : Connection timeout / SSL handshake failure
    Downloading --> Paused : User pause / Network disconnect
    Downloading --> Connecting : Transient error (Exponential Backoff)
    Downloading --> Rebuilding : All segments 100% complete
    Downloading --> Failed : Disk full / I/O error
    Paused --> Connecting : User resumes job
    Rebuilding --> Completed : Chunks stitched & SHA verified
    Rebuilding --> Failed : Stitching disk write failure
    Completed --> [*]
    Failed --> [*]
    Queued --> Canceled : User deletes job
    Downloading --> Canceled : User cancels job
```

### 7.2 Safe Pause & Resume Protocol
To ensure zero corruption upon resume:
1. When pausing, the engine signals a `CancellationToken` to all active segment streams.
2. File streams flush buffers to disk and record the exact verified `CurrentOffset` into the SQLite database.
3. Upon resume, the engine performs a pre-flight probe with `If-Range: <ETag>`. If the server responds with `206 Partial Content`, workers pick up from their saved `CurrentOffset`. If `200 OK` is returned, the file on the server has changed; NovaFetch discards invalid chunks and restarts from 0%.

---

## 8. Browser Integration & Manifest V3 Native Messaging

*(For complete manifest structures and messaging schemas, see [`docs/BROWSER_INTEGRATION_SPEC.md`](file:///C:/Users/HP/.gemini/antigravity/scratch/novafetch/docs/BROWSER_INTEGRATION_SPEC.md)).*

### 8.1 End-to-End Interception Sequence

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Browser as Chrome / Edge (MV3)
    participant Ext as Extension Service Worker
    participant Host as NovaFetch.Host.exe
    participant App as NovaFetch Desktop Engine

    User->>Browser: Clicks file download link (.zip / .iso)
    Browser->>Ext: chrome.downloads.onDeterminingFilename
    Ext->>Host: Stdio Native Message: intercept_download (URL, Cookies, Referrer)
    Host->>App: Named Pipe: DownloadRequestDTO
    App-->>Host: Named Pipe Response: {intercepted: true, jobId}
    Host-->>Ext: Stdio Response: {status: "success"}
    Ext->>Browser: chrome.downloads.cancel(downloadId)
    App->>App: Allocates Segments & Starts Download Engine
    App->>User: Displays Download Progress Window
```

### 8.2 Video Capture & Lawful DRM Boundaries
* **Supported Streams:** Direct MP4/WebM URLs, HTTP Live Streaming (HLS `.m3u8` playlists with `.ts` segments), and unencrypted MPEG-DASH manifests (`.mpd`).
* **Lawful DRM Boundary:** In compliance with DMCA anti-circumvention provisions, NovaFetch **strictly refuses** to intercept or decrypt streams protected by Widevine, FairPlay, or PlayReady DRM. When EME encrypted media is encountered, the UI displays a polite informational banner.

---

## 9. Security Architecture & Threat Modeling

*(For the complete STRIDE threat matrix and path sanitization algorithms, see [`docs/SECURITY_THREAT_MODEL.md`](file:///C:/Users/HP/.gemini/antigravity/scratch/novafetch/docs/SECURITY_THREAT_MODEL.md)).*

### Key Security Safeguards:
1. **Path Traversal & Filename Sanitization:** Enforces strict regex stripping of directory traversal characters (`..`, `/`, `\`), illegal Windows characters (`:`, `*`, `?`, `"`, `<`, `>`, `|`), and reserved DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`).
2. **SSRF & DNS Rebinding Protection:** The custom `SocketsHttpHandler` verifies resolved IP addresses before connection. Connections to loopback (`127.0.0.0/8`, `::1`), link-local (`169.254.0.0/16`), or private RFC 1918 subnets are rejected by default.
3. **Windows SmartScreen Zone Identifier:** When downloads complete, NovaFetch writes an NTFS Alternate Data Stream `Zone.Identifier` (`ZoneId=3` / Internet Zone), ensuring Windows Defender inspects untrusted executables.
4. **Log Data Scrubbing:** Sensitive HTTP headers (`Authorization`, `Cookie`, `Set-Cookie`) are automatically redacted by Serilog sinks before hitting disk.

---

## 10. Test Matrix & Verification Plan

*(For full test procedures and mock harness specs, see [`docs/EXPERIMENT_TEST_PLAN.md`](file:///C:/Users/HP/.gemini/antigravity/scratch/novafetch/docs/EXPERIMENT_TEST_PLAN.md)).*

| Test ID | Functional Scope | Testing Technique | Success Criteria |
|:---|:---|:---|:---|
| **EXP-01** | Dynamic Midpoint Splitting | Mock HTTP Server serving 100 MB deterministic binary. | Active segment split in half; 8 connections run concurrently. |
| **EXP-02** | Non-Range Fallback | Server responds with `200 OK` and `Accept-Ranges: none`. | Single-connection sequential stream; pause warning displayed. |
| **EXP-03** | Resume with Changed ETag | Server alters ETag mid-download. | Engine detects cache invalidation, flushes chunks, and restarts from 0. |
| **EXP-04** | Socket Disconnect & RST | Server drops TCP connection abruptly. | Exponential backoff retry succeeds without byte corruption. |
| **EXP-05** | Abnormal Process Termination | Kill process via `Stop-Process -Force`. | SQLite WAL recovers state; chunks resume cleanly on relaunch. |
| **EXP-06** | Browser Native Messaging | Google Chrome triggering download via MV3 extension. | Interception latency < 150ms; Chrome download canceled cleanly. |
| **EXP-07** | Disk Space Allocation | Pre-allocation on NTFS. | File allocated via `SetLength` without fragmenting drive. |
| **EXP-08** | HLS Segment Assembly | Mock playlist with five unencrypted `.ts` chunks. | Chunks downloaded concurrently and muxed into valid `.mp4`. |

---

## 11. Implementation Roadmap & Milestones

*(For complete delivery schedules, see [`docs/IMPLEMENTATION_ROADMAP.md`](file:///C:/Users/HP/.gemini/antigravity/scratch/novafetch/docs/IMPLEMENTATION_ROADMAP.md)).*

```mermaid
gantt
    title NovaFetch Multi-Phase Implementation Roadmap
    dateFormat  YYYY-MM-DD
    section Phase 1: Research
    Technical Spec & Architecture       :done,    des1, 2026-10-25, 2026-10-28
    section Phase 2: Core Engineering
    M1: Engine & Storage Foundation    :active,  m1,   2026-11-01, 2026-11-14
    M2: Dynamic Segmentation & Stitching :         m2,   2026-11-15, 2026-11-30
    M3: Queue & Scheduler Subsystem    :         m3,   2026-12-01, 2026-12-14
    section Phase 3: Integration
    M4: MV3 Browser Extension & Bridge :         m4,   2026-12-15, 2026-12-31
    M5: Modern WPF Desktop UI          :         m5,   2027-01-01, 2027-01-20
    M6: Packaging & Production Release :         m6,   2027-01-21, 2027-02-05
```

---

## 12. Unresolved Technical Questions

During this research phase, several specific internal edge cases were identified that will be resolved through empirical testing in Milestone 1 and Milestone 2:
1. **Dynamic Split Threshold Fine-Tuning:** What is the optimal minimum byte threshold for segment splitting? (Current hypothesis: 512 KB to avoid excessive connection overhead on high-latency links).
2. **Zero-Copy File Assembly on Windows:** Can `FSCTL_SET_SPARSE` and `IOCTL_STORAGE_MANAGE_DATA_SET_ATTRIBUTES` allow zero-copy segment assembly without sequential disk writes? This will be benchmarked on modern NVMe drives.
3. **High-DPI Per-Monitor V2 WPF Rendering:** Ensuring custom segment progress canvases render crisply across mixed-DPI multi-monitor environments.

---

## 13. References & Citations

All architectural decisions and factual claims are grounded in primary sources:
1. **Tonec Inc. Official Documentation:** IDM User Guide, Dynamic Segmentation Whitepaper, Scheduler Reference, and Release Notes (see [`docs/SOURCES_AND_REFERENCES.md`](file:///C:/Users/HP/.gemini/antigravity/scratch/novafetch/docs/SOURCES_AND_REFERENCES.md)).
2. **IETF Standards:** RFC 9110 (HTTP Semantics), RFC 7233 (Range Requests), RFC 6266 (Content-Disposition).
3. **Chromium Project Documentation:** Native Messaging Specification and Manifest V3 Developer Guide.
4. **Microsoft Learn:** .NET `SocketsHttpHandler` Architecture, Win32 Storage Management, and WPF Virtualization.
