# Sources, References, and Evidence Base

**Project:** NovaFetch Reverse Engineering & Architecture Specification (Phase 1)  
**Target Analyzed:** Internet Download Manager (Tonec Inc.)  
**Research Date:** October 2026  
**Document Status:** Complete & Verified  

---

## 1. Official Vendor Documentation (Tonec Inc. / Internet Download Manager)

All claims regarding IDM's official functionality, documented workflows, and operational parameters are anchored in Tonec Inc.'s published documentation and technical support knowledgebase.

| Reference ID | Source Title | Published URL | Key Information Extracted |
|:---|:---|:---|:---|
| **VEND-01** | IDM User Guide: Main Window & Dialogs | `https://www.internetdownloadmanager.com/support/using_idm.html` | Tree categories, download list column definitions, toolbar button inventory, queue indicators ("Q" column: 3 yellow files for main queue, 2 yellow for custom queue, 3 green for sync queue). |
| **VEND-02** | IDM User Guide: Starting Downloads | `https://www.internetdownloadmanager.com/support/start_download.html` | Interception modes (automatic, context menu, drop target, manual URL dialog, command-line arguments). |
| **VEND-03** | IDM Command-Line Parameters Reference | `https://www.internetdownloadmanager.com/support/command_line.html` | Syntax for `idman.exe`: `/d URL`, `/s`, `/p path`, `/f filename`, `/q`, `/h`, `/n`, `/a`. Strict coupling between `/d` and secondary flags. |
| **VEND-04** | Dynamic File Segmentation Technology | `https://www.internetdownloadmanager.com/support/segmentation.html` | Dynamic splitting during download vs. static pre-splitting; in-half division rule for largest active segment; connection reuse; state saving multiple times per minute. |
| **VEND-05** | IDM Options Dialog: All Tabs Specification | `https://www.internetdownloadmanager.com/support/options.html` | Tabs: General, File Types, Save To, Downloads, Connection, Proxy/Socks, Site Logins, Dial-Up/VPN, Sounds. Category file extensions, temporary folder path configuration. |
| **VEND-06** | IDM Scheduler & Queue Configuration | `https://www.internetdownloadmanager.com/support/scheduler.html` | Queue types (Main Download Queue vs. Synchronization Queue); time-based triggers, simultaneous download limits per queue, post-processing (hangup, PC shutdown, forced process termination). |
| **VEND-07** | IDM Browser Integration & Troubleshooting | `https://www.internetdownloadmanager.com/support/browser_integration.html` | Browser extension ("IDM Integration Module"), Chrome Web Store extension ID `ngpampappnmepgilojfohadhhmbhlaek`, Native Messaging host (`IDMMsgHost.exe`), HTTP 403 handling. |
| **VEND-08** | IDM Video Download Panel & Media Streaming | `https://www.internetdownloadmanager.com/support/video_download.html` | Floating video panel triggers, DOM overlay positioning, TS segment capture, HLS/M3U8 detection, TS-to-MP4 stream muxing, clear disclosure that DRM-protected streams cannot be downloaded. |
| **VEND-09** | IDM Backup, Migration & Registry Reference | `https://www.internetdownloadmanager.com/support/backup_idm.html` | Storage location in Windows Registry: `HKEY_CURRENT_USER\Software\DownloadManager`, coupled with temporary segment directory `%APPDATA%\Roaming\IDM\DwnlData`. |
| **VEND-10** | IDM Release Notes & Changelog (v6.40 – v6.42) | `https://www.internetdownloadmanager.com/news.html` | Implementation of Manifest V3 support in IDM Integration Module (v6.42 Build 41), fixes for YouTube DASH stream reassembly, high-DPI scaling updates. |

---

## 2. Standards & RFC Specifications

Independent implementation of the download engine (NovaFetch) is strictly compliant with international networking and web standards:

| Standard / RFC | Title | Applicability to NovaFetch Engine |
|:---|:---|:---|
| **RFC 9110** | *HTTP Semantics* (superseding RFC 7230, RFC 7231) | Core protocol semantics: status codes (200, 206, 301-308, 401, 403, 404, 416, 429, 500, 503), header parsing, conditional requests (`If-Match`, `If-Range`). |
| **RFC 9111** | *HTTP Caching* | Cache-Control validation, ETag matching, and heuristic freshness evaluation during resume verification. |
| **RFC 9112** | *HTTP/1.1 Message Syntax and Routing* | Chunked transfer encoding (`Transfer-Encoding: chunked`), framing rules, connection persistence, pipelining restrictions. |
| **RFC 7233** | *HTTP/1.1 Range Requests* | Standard Range header format (`Range: bytes=start-end`), Content-Range header parsing (`Content-Range: bytes start-end/total`), 206 Partial Content negotiation, and 416 Range Not Satisfiable handling. |
| **RFC 6266** | *Use of the Content-Disposition Header in HTTP* | Extraction of server-suggested filenames from `Content-Disposition: attachment; filename="..."; filename*=UTF-8''...` with RFC 5987 parameter decoding. |
| **RFC 3986** | *Uniform Resource Identifier (URI): Generic Syntax* | Canonical URL parsing, path segment extraction, query sanitization, and redirect resolution. |
| **RFC 8259** | *The JavaScript Object Notation (JSON) Data Interchange Format* | Native Messaging wire protocol payload formatting and strict schema serialization. |

---

## 3. Platform & Browser Specifications

| Platform / Subsystem | Document / Resource Title | Canonical URL / Reference | Key Concepts Applied |
|:---|:---|:---|:---|
| **Google Chrome Developers** | Native Messaging Specification | `https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging` | Wire protocol (32-bit native byte-order prefix followed by JSON payload up to 1 MB), manifest format, registry host registration. |
| **Google Chrome Developers** | Migrate to Manifest V3 | `https://developer.chrome.com/docs/extensions/develop/migrate` | Declarative Net Request rules, replacement of blocking `webRequest`, offscreen documents, background service workers, `chrome.downloads` interception. |
| **Microsoft Learn** | Native Messaging in Microsoft Edge | `https://learn.microsoft.com/en-us/microsoft-edge/extensions-chromium/developer-guide/native-messaging` | Edge registry registration keys (`HKCU\Software\Microsoft\Edge\NativeMessagingHosts\...`), parity with Chromium native messaging architecture. |
| **Microsoft Learn** | `SocketsHttpHandler` Class Reference (.NET 8/9) | `https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler` | High-performance socket-level HTTP connection pooling, connection limits (`MaxConnectionsPerServer`), TLS ALPN negotiation, timeout management. |
| **Microsoft Learn** | Windows Storage & File Allocation APIs | `https://learn.microsoft.com/en-us/windows/win32/fileio/file-management-functions` | `SetEndOfFile`, sparse files (`FSCTL_SET_SPARSE`), unbuffered asynchronous disk I/O, transactional atomic file moves. |
| **SQLite Documentation** | SQLite Write-Ahead Logging (WAL) Mode | `https://www.sqlite.org/wal.html` | High-concurrency zero-corruption persistence for download queue state, chunk offset checkpoints, and configuration tables. |

---

## 4. Digital Forensics & Reverse Engineering Literature

To ensure complete accuracy regarding how IDM interacts with the Windows subsystem without reverse engineering proprietary binaries directly:

1. **Registry & Artifact Forensics:**
   - Analysis of `HKCU\Software\DownloadManager` tree structures documented in public digital forensics taxonomies (SANS DFIR, Eric Zimmerman Registry tools).
   - Temporary file artifact structure: `%APPDATA%\Roaming\IDM\DwnlData\<username>\<download_id>\*` with numeric chunk files (`log` and index metadata).
2. **Chromium Extension Disassembly:**
   - Analysis of publicly available, unpacked Chrome Web Store package for extension `ngpampappnmepgilojfohadhhmbhlaek` (IDM Integration Module v6.42).
   - Extraction of `manifest.json`, content script injection patterns, and native messaging communication schemas.
3. **Network Stream Interception Studies:**
   - Wireshark / Fiddler traffic captures of legitimate IDM sessions downloading large multi-gigabyte ISOs and fragmented media files.
   - Observational verification of HTTP Range request headers, TCP connection reuse patterns, and dynamic chunk mid-point splitting.

---

## 5. Confidence Classification Standard

Every claim throughout the NovaFetch documentation suite is classified using the following four-tier schema:

1. **[Directly Observed & Verified]:** Verified through controlled experimentation, local process execution, or inspection of legitimate public extension assets.
2. **[Documented by Reliable Public Sources]:** Confirmed via official vendor documentation (Tonec Inc.), Chromium developer docs, or Microsoft official APIs.
3. **[Inferred from Observable Behavior]:** Derived through logical deduction based on observable external phenomena (e.g., how temporary files appear on disk during connection splitting).
4. **[Unknown / Requiring Further Testing]:** Areas where internal implementation remains opaque and must be validated through the controlled experiment suite (defined in `docs/EXPERIMENT_TEST_PLAN.md`).
