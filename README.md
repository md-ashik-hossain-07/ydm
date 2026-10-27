# YDM — Your Download Manager

> **Your Downloads. Your Control.**

[![Build & Test](https://github.com/ydm-downloader/ydm/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/ydm-downloader/ydm/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows 10/11](https://img.shields.io/badge/Platform-Windows%2010%2F11%20x64-brightgreen.svg)](#prerequisites)
[![Target Framework: .NET 8.0](https://img.shields.io/badge/.NET-8.0%20LTS-purple.svg)](#development-setup)

**YDM (Your Download Manager)** is an independent, high-performance, open-source download manager built for Windows 10 and Windows 11. Designed from clean-room architectural principles, YDM provides reliable multi-connection downloads, robust pause and resume capabilities, SQLite-backed state persistence, queue management, and privacy-first Chromium browser integration.

---

## 1. Key Features

- **Multi-Connection HTTP Range Acceleration:** Automatically probes remote server capabilities (`206 Partial Content`, `Accept-Ranges: bytes`) and splits files into parallel connection streams.
- **Dynamic In-Flight Segmentation:** Divides the largest active incomplete segment at its midpoint during download, keeping connections saturated without gaps or boundary collisions.
- **Resilient Pause & Resume:** Interrupted or paused downloads resume from disk checkpoints without data corruption. Validates remote server state via `ETag` and `Last-Modified` timestamps.
- **Graceful Single-Stream Fallback:** If a remote host ignores Range requests or serves non-resumable content (`200 OK`), YDM automatically falls back to single-stream sequential downloading.
- **ACID Persistence (SQLite WAL):** Download histories, segment offsets, and queue orders persist safely in SQLite Write-Ahead Logging (WAL) mode, guaranteeing survival across power losses or application crashes.
- **Queue & Priority Management:** Concurrency throttler (`SemaphoreSlim`) limits active downloads (default: 2), manages priorities (`High`, `Normal`, `Low`), and auto-dequeues pending jobs.
- **Security & Privacy by Design:**
  - Strict path sanitization (`PathSanitizer`) strips directory traversals (`../`, `..\`) and reserved DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`).
  - SSRF protection blocks private and loopback IP addresses by default.
  - Attaches NTFS `Zone.Identifier` (`ZoneId=3`) so Windows SmartScreen and Defender inspect downloaded executables.
  - Zero telemetry, zero analytics tracking, and no complete browser cookie jar harvesting.
- **Chromium Manifest V3 Integration:** Lightweight extension for Chrome and Edge communicating over sandboxed Native Messaging (`stdio`). Only cancels browser downloads after YDM acknowledges job acceptance.

---

## 2. Architecture Overview

YDM is built as a modular solution in C# 12 and .NET 8.0 LTS:

| Project | Responsibility |
|:---|:---|
| [`YDM.Core`](src/YDM.Core) | Domain models (`DownloadJob`, `DownloadSegment`), state machines, interfaces, and security sanitizers (`PathSanitizer`, `UrlSecurityValidator`). |
| [`YDM.Storage`](src/YDM.Storage) | SQLite persistence, schema migrations (`MigrationService`), and repositories (`SqliteDownloadRepository`, `SqliteSettingsRepository`). |
| [`YDM.Engine`](src/YDM.Engine) | HTTP Range engine, `HttpProbeService`, `DynamicSegmentSplitter`, `SegmentDownloader`, and `FileAssembler`. |
| [`YDM.Queue`](src/YDM.Queue) | Concurrency throttler (`DownloadQueueManager`), priority scheduling, and queue state orchestration. |
| [`YDM.UI`](src/YDM.UI) | Modern Fluent WPF desktop client with virtualized download grid, segment visualizer, categories tree, and dialogs. |
| [`YDM.Host`](src/YDM.Host) | Chromium Native Messaging console bridge communicating over binary `stdio` and Named Pipes (`\\.\pipe\YDM_IPC`). |
| [`YDM.Tests`](tests/YDM.Tests) | Comprehensive automated test suite with an in-process local HTTP mock server. |

---

## 3. Prerequisites

- **Operating System:** Windows 10 (version 1809+) or Windows 11 (64-bit).
- **Runtime:** .NET 8.0 Desktop Runtime (included automatically in self-contained releases).
- **SDK for Developers:** [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher.

---

## 4. Installation Options

### Option A: Windows Installer (Recommended)
1. Download the latest installer `ydm_setup_1.0.0_win-x64.exe` from [GitHub Releases](https://github.com/ydm-downloader/ydm/releases).
2. Run the installer and follow the setup wizard.
3. *Note on Windows SmartScreen:* Initial releases are distributed unsigned. If Windows SmartScreen displays a warning, click **More info** -> **Run anyway**.

### Option B: PowerShell Fast Install
To download, verify SHA-256 integrity, and install via PowerShell:
```powershell
irm https://raw.githubusercontent.com/ydm-downloader/ydm/main/installer/install.ps1 | iex
```

### Option C: Portable ZIP
Download `ydm-v1.0.0-win-x64-portable.zip` from Releases, extract to any folder, and run `YDM.UI.exe`.

### Option D: Build from Source
```powershell
git clone https://github.com/ydm-downloader/ydm.git
cd ydm
dotnet restore YDM.sln
dotnet build YDM.sln --configuration Release
dotnet run --project src/YDM.UI/YDM.UI.csproj
```

---

## 5. Browser Extension Setup (Chrome & Edge)

To enable automatic download interception and context menu integration:

1. **Register the Native Messaging Host:**
   Run the registration script with PowerShell:
   ```powershell
   pwsh host/register_host.ps1
   ```
2. **Load the Extension:**
   - In Google Chrome, navigate to `chrome://extensions/` (or `edge://extensions/` in Microsoft Edge).
   - Enable **Developer mode** (toggle in top right).
   - Click **Load unpacked** and select the [`extension/`](extension/) directory in this repository.
3. **Verify Connection:**
   - Click the YDM icon in your browser toolbar. The status badge will display **YDM Desktop App: Active**.

---

## 6. Running the Automated Test Suite

YDM includes 37 deterministic automated unit and integration tests executing against an in-process mock HTTP server:

```powershell
dotnet test tests/YDM.Tests/YDM.Tests.csproj --configuration Release
```

Test coverage includes:
- Multi-connection partial content downloads and cryptographic SHA-256 verification.
- Graceful single-stream degradation on non-range servers (`Accept-Ranges: none`).
- Pause and resume integrity without data loss.
- Mid-download server ETag change detection and safe segment invalidation.
- Path traversal rejection and Windows DOS device name sanitization.
- SSRF loopback and private IP blocking.
- Database state persistence and process restart recovery.

---

## 7. Known Limitations & Boundaries

- **DRM Protection:** YDM **does not** and **will not** bypass Digital Rights Management (DRM) or access control paywalls (e.g., Widevine, FairPlay, PlayReady). Encrypted streams cannot be downloaded.
- **Speed Expectations:** Parallel connections improve throughput on high-latency links where single TCP streams underutilize bandwidth. They do not bypass remote server-side bandwidth caps or network infrastructure limits.
- **Platform Scope:** The desktop UI currently targets Windows 10/11 x64 via WPF.

---

## 8. Security & Vulnerability Reporting

Please review our [SECURITY.md](SECURITY.md) policy for vulnerability reporting guidelines. To report a security vulnerability, please submit a private advisory via GitHub Security Advisories.

---

## 9. Legal Disclaimer

YDM is an independent, clean-room open-source project. It is **not affiliated with, endorsed by, or associated with** Tonec Inc. or Internet Download Manager (IDM). All product names, logos, and brands are property of their respective owners.

---

## 10. License

YDM is licensed under the [MIT License](LICENSE).
