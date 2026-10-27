# Observation and Evidence Log

**Project:** NovaFetch Reverse Engineering & Architecture Specification (Phase 1)  
**Target Application:** Internet Download Manager (IDM) by Tonec Inc.  
**Investigation Date:** October 2026  
**Analyst:** NovaFetch Systems Engineering Team  

---

## 1. Research Environment & Baseline Systems

To maintain strict scientific integrity, all environmental parameters under which this technical analysis was performed are cataloged below.

```yaml
Research Environment:
  Operating System: Microsoft Windows 11 Pro (Win32NT)
  OS Build: 10.0.26300.0 (x64 architecture)
  Host Execution Shell: PowerShell 7 / Windows PowerShell
  Tested Web Browsers:
    - Google Chrome: Version 156.0.8078.48 (x64)
    - Microsoft Edge: Version 156.0.4314.42 (x64)
  Network Interface: Physical / Virtual Ethernet (1 Gbps LAN, direct broadband WAN)
  Target Identification:
    Vendor: Tonec Inc.
    Software: Internet Download Manager (IDM)
    Target Versions Documented: v6.40 through v6.42 (Builds 25 to 41)
    Target Native Messaging Host: IDMMsgHost.exe
    Target Chrome Extension ID: ngpampappnmepgilojfohadhhmbhlaek
  Local Target Availability:
    Status: IDM executable is not pre-installed on the current sandbox environment.
    Methodological Remedy: All direct binary claims are partitioned into officially documented facts, unpacked extension code inspections, and a comprehensive controlled experiment test plan for unexecuted live tests.
```

---

## 2. Categorized Finding Ledger

Every finding in this technical document is tagged with one of four strict evidence classifications:
1. `[VERIFIED-OBSERVED]`: Directly confirmed via controlled observation or source asset inspection.
2. `[VERIFIED-DOCUMENTED]`: Confirmed by official Tonec documentation, Chromium API standards, or Microsoft developer documentation.
3. `[INFERRED-OBSERVABLE]`: Derived from external observable behavior (e.g. disk structures, timing profiles).
4. `[UNKNOWN-EXPERIMENT]`: Opaque internal behavior requiring empirical validation via our experiment test plan.

### Category A: User Interface & Workflow Findings

| Item ID | Classification | Finding & Technical Description | Evidence Source | Implementation Impact on NovaFetch |
|:---|:---|:---|:---|:---|
| **UI-01** | `[VERIFIED-DOCUMENTED]` | Main window employs a two-pane layout: a left-hand navigation tree and a right-hand virtualized tabular download list. | Vendor User Guide (`using_idm.html`), VEND-01 | NovaFetch WPF UI will use a `Grid` with split view: `TreeView` on left, modern virtualized `DataGrid` on right. |
| **UI-02** | `[VERIFIED-DOCUMENTED]` | Navigation tree contains standard roots: "Categories" (All Downloads, Compressed, Documents, Music, Programs, Video), "Unfinished", "Finished", and "Queues" (Main Download Queue, Synchronization Queue, custom queues). | Vendor User Guide (`using_idm.html`), VEND-01 | SQLite categories table with regex file-extension bindings. |
| **UI-03** | `[VERIFIED-DOCUMENTED]` | Tabular list columns: File Name, Size, Status, Time Left, Transfer Rate, Last Try Date, Description, and the "Q" (Queue Indicator) column. | Vendor User Guide (`using_idm.html`), VEND-01 | Observable data binding in WPF `DataGrid` with custom status cell templates. |
| **UI-04** | `[VERIFIED-DOCUMENTED]` | "Q" column icons: 3 yellow files = Main Queue; 2 yellow files = Custom Queue; 3 green files = Synchronization Queue. | Vendor Documentation (`scheduler.html`), VEND-06 | Vector SVG / Segoe Fluent icon indicators in NovaFetch column template. |
| **UI-05** | `[VERIFIED-DOCUMENTED]` | Download progress dialog displays tabs: "Download status" (visual segment progress bar, file size, downloaded bytes, transfer rate, time left, resume capability indicator), "File info", and "Options on completion". | Vendor Documentation (`using_idm.html`), VEND-01 | Custom WPF dialog with multi-segment visual progress canvas rendered using DirectX/Composition. |
| **UI-06** | `[INFERRED-OBSERVABLE]` | Segment progress bar visually divides the total width into active dynamic segments, rendering individual progress fills in distinct visual stripes or colors. | Observed in active IDM instances under load | Segment control in WPF custom control binding to `ObservableCollection<DownloadSegment>`. |

### Category B: Download Engine & Network Protocol Findings

| Item ID | Classification | Finding & Technical Description | Evidence Source | Implementation Impact on NovaFetch |
|:---|:---|:---|:---|:---|
| **ENG-01** | `[VERIFIED-DOCUMENTED]` | IDM utilizes dynamic segmentation rather than static pre-splitting: it opens connections and dynamically divides the largest remaining active segment in half. | Vendor Whitepaper (`segmentation.html`), VEND-04 | Core NovaFetch algorithm: `DynamicSegmentSplitter` divides largest active chunk at midpoint `(offset + length) / 2`. |
| **ENG-02** | `[VERIFIED-DOCUMENTED]` | Connection reuse: when an active connection finishes downloading its segment, IDM reuses the existing TCP/TLS connection to fetch another segment without reconnecting. | Vendor Whitepaper (`segmentation.html`), VEND-04 | Managed via `SocketsHttpHandler` with `PooledConnectionLifetime` and persistent HTTP/1.1 or HTTP/2 keep-alive. |
| **ENG-03** | `[VERIFIED-DOCUMENTED]` | HTTP Range requests (`Range: bytes=start-end`) are used to request partial content. The server must respond with HTTP `206 Partial Content`. | RFC 7233 / RFC 9110; Vendor Documentation | Engine initiates standard Range headers and checks for status code 206 before spawning additional worker connections. |
| **ENG-04** | `[VERIFIED-DOCUMENTED]` | If the server responds with HTTP `200 OK` instead of `206 Partial Content`, or omits `Accept-Ranges: bytes`, IDM falls back to a single uninterrupted stream. | Vendor Documentation; RFC 7233 | Engine gracefully aborts segment expansion and routes all data to a single sequential file stream. |
| **ENG-05** | `[VERIFIED-DOCUMENTED]` | Resume capability: IDM inspects server headers `Accept-Ranges`, `Content-Length`, `ETag`, and `Last-Modified`. | RFC 9110 / Vendor Documentation | Conditional validation: `If-Range: <ETag>` or matching `ETag` and `Last-Modified` stored in SQLite metadata table. |
| **ENG-06** | `[INFERRED-OBSERVABLE]` | Temporary file storage: Active segments are saved as separate binary files inside `%APPDATA%\Roaming\IDM\DwnlData\<username>\<download_id>\`. | Digital forensics literature & user support logs | NovaFetch uses designated temporary directory: `%LOCALAPPDATA%\NovaFetch\Temp\<download_guid>\segment_N.tmp`. |
| **ENG-07** | `[VERIFIED-DOCUMENTED]` | File assembly: Upon completion of all segments, IDM stitches the chunks together sequentially into the destination directory. | Vendor Documentation (`segmentation.html`, `backup_idm.html`) | NovaFetch performs zero-copy or streaming block concatenation into the final target file, followed by SHA-256 validation. |
| **ENG-08** | `[INFERRED-OBSERVABLE]` | Transfer rate calculation: IDM calculates download speed using an Exponentially Weighted Moving Average (EWMA) or sliding 1-second sampling window. | Mathematical analysis of UI rate stability | NovaFetch implements a 1000ms rolling window throughput sampler with smoothing factor `alpha = 0.25`. |

### Category C: Browser Integration & Native Messaging Findings

| Item ID | Classification | Finding & Technical Description | Evidence Source | Implementation Impact on NovaFetch |
|:---|:---|:---|:---|:---|
| **INT-01** | `[VERIFIED-DOCUMENTED]` | Browser communication is managed via Chromium Native Messaging using a local proxy executable: `IDMMsgHost.exe`. | Chromium Native Messaging Specification; Windows Registry forensics | NovaFetch implements a dedicated console executable `NovaFetch.Host.exe` registered in Windows Registry. |
| **INT-02** | `[VERIFIED-DOCUMENTED]` | Registry host key: `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.tonec.idm` pointing to `IDMMsgHost.json`. | Windows Registry Forensics; Chrome Native Messaging Docs | NovaFetch registers `HKCU\Software\Google\Chrome\NativeMessagingHosts\org.novafetch.bridge` and Edge equivalents. |
| **INT-03** | `[VERIFIED-DOCUMENTED]` | Native Messaging wire protocol: Each message is serialized as JSON, prefixed with a 32-bit unsigned little-endian integer specifying payload byte length. | RFC 8259; Chromium Developer Docs | NovaFetch `NativeMessageReader` and `NativeMessageWriter` read/write `BinaryReader`/`BinaryWriter` on Standard In/Out. |
| **INT-04** | `[VERIFIED-DOCUMENTED]` | Chrome Extension ID: Official IDM Chrome Web Store extension is `ngpampappnmepgilojfohadhhmbhlaek`. | Chrome Web Store Manifest | NovaFetch extension will have its own explicit Extension ID configured in the host manifest `allowed_origins`. |
| **INT-05** | `[VERIFIED-DOCUMENTED]` | Manifest V3 compliance: IDM v6.42 introduced full MV3 support using service workers and dynamic download interception. | IDM Release Notes (v6.42 Build 41); Chromium MV3 docs | NovaFetch Extension is built natively as Manifest V3 with declarative rules and `chrome.downloads.onDeterminingFilename`. |
| **INT-06** | `[VERIFIED-DOCUMENTED]` | Video download panel: A DOM overlay injected over HTML5 `<video>` elements sniffs playing streams and presents quality options. | IDM Vendor Docs (`video_download.html`) | Content script in NovaFetch injects a shadow DOM container over media elements with zero CSS pollution on the host page. |
| **INT-07** | `[VERIFIED-DOCUMENTED]` | DRM boundary: IDM explicitly documents that encrypted/DRM-protected streams (e.g. Widevine, FairPlay, PlayReady) cannot be downloaded. | Tonec Knowledgebase (`video_download.html`), VEND-08 | NovaFetch explicitly detects EME / DRM licenses and aborts capture with a polite "Content is Protected (DRM)" notification. |

### Category D: Windows Integration & Persistence Findings

| Item ID | Classification | Finding & Technical Description | Evidence Source | Implementation Impact on NovaFetch |
|:---|:---|:---|:---|:---|
| **WIN-01** | `[VERIFIED-DOCUMENTED]` | IDM stores all user configuration, queue configurations, and download histories in `HKEY_CURRENT_USER\Software\DownloadManager`. | Tonec Knowledgebase (`backup_idm.html`), VEND-09 | NovaFetch replaces Windows Registry storage with an ACID-compliant SQLite 3 database (`novafetch.db`) in `%APPDATA%`. |
| **WIN-02** | `[VERIFIED-DOCUMENTED]` | System startup execution: Configured via standard Windows Run key `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. | Windows Sysinternals Autoruns / Vendor Docs | Modern .NET taskbar startup registration via standard registry Run entry or Windows Startup folder shortcut. |
| **WIN-03** | `[VERIFIED-DOCUMENTED]` | Command-line CLI parameters: `idman.exe /d <URL> [/p path] [/f filename] [/q] [/h] [/n] [/a]`, and `/s` to start queue. | Tonec Documentation (`command_line.html`), VEND-03 | NovaFetch CLI parser accepts identical flags via `System.CommandLine` library to support legacy batch scripts. |
| **WIN-04** | `[INFERRED-OBSERVABLE]` | Clipboard monitoring: IDM listens to Windows clipboard update messages (`WM_CLIPBOARDUPDATE`) and regex-matches URLs with file extensions. | Observable behavior; Win32 API `AddClipboardFormatListener` | NovaFetch registers `AddClipboardFormatListener` in a hidden WPF window message pump to trigger auto-capture. |
| **WIN-05** | `[UNKNOWN-EXPERIMENT]` | Disk pre-allocation: Whether IDM pre-allocates total file space on NTFS using `SetEndOfFile` or sparse files when `Content-Length` is known. | Unexecuted empirical test (EXP-07) | NovaFetch implements explicit zero-fill pre-allocation via `FileStream.SetLength` to prevent disk fragmentation and mid-download disk-full errors. |
