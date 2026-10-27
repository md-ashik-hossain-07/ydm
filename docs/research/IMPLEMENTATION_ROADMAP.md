# NovaFetch Implementation Roadmap & Milestone Plan

**Project:** NovaFetch (Original Windows Download Manager)  
**Document:** Multi-Phase Implementation Roadmap  
**Target Delivery:** Phased Engineering Pipeline (M1 through M6)  

---

## 1. Project Organization & Solution Structure

The solution will be organized into clean, loosely-coupled .NET projects:

```text
NovaFetch/
├── src/
│   ├── NovaFetch.Core/              # Domain models, state machine, interfaces, security
│   ├── NovaFetch.Engine/            # HTTP Range engine, dynamic segmentation, file assembler
│   ├── NovaFetch.Storage/           # SQLite database, migrations, repository layer
│   ├── NovaFetch.Queue/             # Scheduling service, queue priority workers
│   ├── NovaFetch.Host/              # Console Native Messaging stdio bridge executable
│   ├── NovaFetch.UI/                # WPF desktop application (MVVM, Fluent styling)
│   └── NovaFetch.Extension/         # Chrome/Edge Manifest V3 extension source
├── tests/
│   ├── NovaFetch.Core.Tests/        # Unit tests for domain logic and security sanitizers
│   ├── NovaFetch.Engine.Tests/      # Network tests with local mock HTTP server
│   └── NovaFetch.Storage.Tests/     # SQLite persistence and concurrency tests
├── build/                           # Inno Setup / WiX v4 installer packaging scripts
└── NovaFetch.sln
```

---

## 2. Milestone Roadmap

### Milestone 1 (M1): Core Engine & Storage Foundation
* **Objective:** Establish domain models, SQLite persistence, and single-stream / basic multi-range HTTP downloading.
* **Deliverables:**
  * `NovaFetch.Core`: Domain interfaces, `DownloadJob`, `DownloadSegment`, `JobStatus` state machine.
  * `NovaFetch.Storage`: SQLite database schema, WAL mode configuration, repository methods.
  * `NovaFetch.Engine`: `HttpProbeService` (RFC 9110 compliant), single-connection file downloader.
  * Unit tests validating Range header generation and database schema persistence.
* **Acceptance Criteria:**
  - Capable of downloading a 50 MB test file from a local HTTP mock server.
  - Job progress and downloaded bytes persist correctly across simulated application restarts.

### Milestone 2 (M2): Dynamic In-Half Segmentation & Stitching
* **Objective:** Implement the signature IDM dynamic splitting algorithm and high-performance chunk reassembly.
* **Deliverables:**
  * `DynamicSegmentSplitter`: Real-time evaluation of largest active segment and midpoint calculation.
  * Multi-connection coordinator: Concurrently running up to 8/16/32 worker connections over `SocketsHttpHandler`.
  * `FileAssembler`: Streaming 1 MB buffer sequential chunk stitching with atomic rename.
  * EWMA speed calculator and time-remaining estimator.
* **Acceptance Criteria:**
  - A 500 MB file downloads across 8 dynamic connections on a simulated throttled server with at least 3x throughput improvement over single connection.
  - Pausing and resuming preserves exact byte boundaries without re-downloading completed chunks.
  - Assembled file SHA-256 hash matches the source file exactly.

### Milestone 3 (M3): Queue Manager & Scheduler Service
* **Objective:** Background queue execution, concurrency throttling, and automated scheduler triggers.
* **Deliverables:**
  * `NovaFetch.Queue`: Main Download Queue, Synchronization Queue, and user custom queues.
  * Concurrency throttler (`SemaphoreSlim`) enforcing simultaneous download limits.
  * Scheduler timer service supporting recurring start/stop times and post-download PC shutdown actions.
* **Acceptance Criteria:**
  - Queue processes 10 queued items sequentially (concurrency = 1) without manual intervention.
  - Scheduler starts and stops queues precisely at configured timestamps.

### Milestone 4 (M4): Manifest V3 Browser Extension & Native Messaging Host
* **Objective:** Transparent integration with Google Chrome and Microsoft Edge.
* **Deliverables:**
  * `NovaFetch.Host`: Lightweight C# console executable reading/writing 32-bit framed JSON over `stdio`.
  * Windows Registry installation script for Chrome and Edge native messaging hosts.
  * Chrome MV3 extension: `background.js` intercepting downloads via `chrome.downloads.onDeterminingFilename`.
  * Content script: HTML5 `<video>` sniffing and floating "Download Video" panel overlay.
* **Acceptance Criteria:**
  - Clicking a `.zip` or `.iso` link in Google Chrome cancels the browser's native download and hands it off to NovaFetch within 150ms.
  - Video download panel appears over playing HTML5 video streams with accurate media URLs.

### Milestone 5 (M5): Modern WPF Desktop Interface (UI)
* **Objective:** High-polish, responsive desktop experience inspired by modern Windows 11 aesthetics.
* **Deliverables:**
  * Main Window: Navigation category tree, virtualized download grid, toolbar, and status bar.
  * Download Progress Window: Multi-colored segmented visual canvas rendering live chunk fills.
  * Add URL dialog, Options configuration dialog (9 tabs), and queue scheduler editor.
  * Windows Shell Integration: System tray icon, clipboard URL listener, and Windows Toast notifications.
* **Acceptance Criteria:**
  - UI maintains a responsive 60fps frame rate while 4 downloads run concurrently at 50 MB/s.
  - DPI changes across multiple monitors render crisp vector icons and readable typography.

### Milestone 6 (M6): Packaging, Installer & Final Verification
* **Objective:** Turnkey Windows distribution, self-contained single-file publishing, and regression test suite.
* **Deliverables:**
  * Self-contained x64 publish profiles (`dotnet publish -c Release -r win-x64 --self-contained`).
  * Inno Setup / WiX installer script that registers Native Messaging hosts, file associations, and start menu shortcuts.
  * End-to-end integration test suite running against local mock server.
* **Acceptance Criteria:**
  - Clean installation on a fresh Windows 11 virtual machine without pre-installed .NET runtimes.
  - Uninstallation cleanly removes all registry keys, Native Messaging entries, and application binaries.
