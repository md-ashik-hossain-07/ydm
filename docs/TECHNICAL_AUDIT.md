# YDM Technical Audit & Evidence Verification

**Document Version:** 2.0.0  
**Project:** YDM (Your Download Manager)  
**Date:** October 2026  
**Auditor:** Lead Software Architect & Implementation Engineer  

---

## 1. Executive Summary

This document performs a comprehensive technical audit of the Phase 1 research documents originally produced under the legacy name *NovaFetch*. The audit evaluates all empirical claims, protocol assumptions, security assertions, and architectural proposals against verifiable technical standards (RFC 9110, RFC 7233, RFC 6266), official platform documentation (Microsoft .NET 8, Chromium Manifest V3), and the real target execution environment.

### Core Audit Findings
1. **Host Environment & IDM Availability:** The research explicitly notes that Internet Download Manager (IDM) was **not installed** in the investigation sandbox. Consequently, all claims regarding internal heuristic algorithms (such as specific segment-split thresholds, thread pool allocations, and socket polling loops) were unexecuted hypotheses rather than proven facts. YDM will implement standards-compliant, verifiable download logic rather than emulating unconfirmed proprietary internals.
2. **Branding & Naming Migration:** The legacy name *NovaFetch* is retired and replaced with **YDM (Your Download Manager)** across all namespaces, identifiers, file paths, extension IDs, and documentation.
3. **Security Deficits in Early Drafts:** Early research drafts suggested passing full browser cookie jars to the desktop process and intercepting browser downloads aggressively. This introduces major privacy and credential-leakage risks. YDM mandates strict zero-leakage policies: no complete cookie jars, no logging of credentials, and strict origin validation on native messaging channels.
4. **Platform & Framework Alignment:** The proposed stack of **C# 12 / .NET 8 LTS (WPF)** with **SQLite 3 (WAL mode)** and **SocketsHttpHandler** is verified to be fully supported, maintainable, and deployable on Windows 10/11 x64.

---

## 2. Evidence Audit of Reverse-Engineering Claims

Each claim from the research documentation is classified into one of six evidentiary tiers:
- **[Directly Observed]**: Verified through local inspection or reproducible runtime testing.
- **[Official Vendor Source]**: Documented in Tonec Inc.'s published technical knowledgebase.
- **[Technical Standard]**: Governed by ratified IETF RFCs or Chromium specifications.
- **[Inference from Behavior]**: Deduced logically from external artifacts (registry keys, disk files).
- **[Untested Hypothesis]**: Unverified claim requiring controlled experimental proof.
- **[Conflicting / Unsupported]**: Inaccurate, outdated, or internally contradictory claim.

| Claim ID | Topic | Detailed Claim | Evidentiary Status | Source / Verification Details | Audit Assessment & Action for YDM |
|:---|:---|:---|:---|:---|:---|
| **AUD-01** | Segmentation | IDM dynamically divides the largest remaining active segment in half at midpoint rather than statically splitting at byte 0. | **[Official Vendor Source]** | Tonec Technical Paper (`segmentation.html`, checked Oct 2026). | Supported by vendor documentation in concept, but exact runtime trigger points and byte thresholds remain unverified. YDM implements configurable dynamic segmentation with a minimum chunk threshold (2 MB) to prevent thrashing. |
| **AUD-02** | Connection Reuse | Existing TCP/TLS connections are kept alive and reused for new segments without renegotiating handshakes. | **[Official Vendor Source] + [Technical Standard]** | Tonec (`segmentation.html`); RFC 9112 (HTTP/1.1 Persistent Connections). | Supported. In .NET 8, `SocketsHttpHandler` manages connection pooling and keep-alive natively via `PooledConnectionLifetime` and `PooledConnectionIdleTimeout`. |
| **AUD-03** | Range Negotiation | Server must return `206 Partial Content` and valid `Content-Range`. If `200 OK` is returned, engine falls back to single connection. | **[Technical Standard]** | RFC 7233 / RFC 9110 (HTTP Range Requests). | Supported. YDM will probe with `Range: bytes=0-0` or inspect `Accept-Ranges` and fallback gracefully to single-stream download when partial content is rejected. |
| **AUD-04** | ETag & Resume Safety | If remote ETag or Last-Modified changes during pause, resumed downloads will corrupt if partial chunks are stitched. | **[Technical Standard]** | RFC 9111 (HTTP Caching) & RFC 9110 (`If-Range`). | Supported. Resuming must check `If-Range: <ETag>`. If the server returns `200 OK`, previous segments are invalidated and redownloaded from 0. |
| **AUD-05** | Browser Interception | Downloads are intercepted via `chrome.downloads.onDeterminingFilename` in Manifest V3. | **[Technical Standard]** | Chromium Developer Documentation (`chrome.downloads` API, checked Oct 2026). | Supported. Interception must only cancel browser downloads after the desktop application explicitly confirms receipt of the download request. |
| **AUD-06** | Native Messaging | Native host uses 32-bit little-endian length prefix with UTF-8 JSON over stdio. | **[Technical Standard]** | Chromium Native Messaging Specification. | Supported. YDM will implement `YDM.Host` using binary stdio framing and restrict origins to the official extension ID. |
| **AUD-07** | Video Sniffing | Injected DOM overlay can intercept all streaming video including protected web content. | **[Conflicting / Unsupported]** | Tonec Knowledgebase (`video_download.html`) explicitly states DRM content cannot be captured. | Corrected: YDM **strictly refuses** DRM circumvention (Widevine, FairPlay, PlayReady) and only supports direct unencrypted URLs and lawful public HLS streams. |
| **AUD-08** | Guaranteed 5x Speed | Multi-connection downloading guarantees a 5x or 10x throughput multiplier on all networks. | **[Untested Hypothesis]** | Marketing claim without reproducible benchmark evidence. | Rejected: Speed depends entirely on remote server bandwidth caps, TCP window scaling, and local latency. YDM will report empirical throughput without making fabricated multiplier claims. |
| **AUD-09** | Registry State Storage | IDM stores all state in Windows Registry `HKCU\Software\DownloadManager`. | **[Inference from Behavior]** | Digital forensics literature & Tonec backup guides. | Acknowledged as legacy IDM behavior, but **rejected for YDM**. Modern desktop apps should use an ACID relational database (SQLite) rather than bloating the Windows Registry. |

---

## 3. Technical Consistency Audit

### 3.1 Naming and Identity Migration
- Legacy identifier *NovaFetch* appeared in multiple files (`NovaFetch.Core`, `NovaFetch.UI`, `NovaFetch.Host`, `novafetch.db`, `org.novafetch.bridge`).
- **Correction:** Unified all namespaces under `YDM`:
  - `YDM.Core` (Models, Interfaces, Security)
  - `YDM.Engine` (HTTP Range, Probing, Dynamic Segmentation, Assembly)
  - `YDM.Storage` (SQLite Database, Migrations, Repositories)
  - `YDM.Queue` (Queue Scheduler, Concurrency Control)
  - `YDM.UI` (WPF Fluent Client, Segment Visualizer, System Tray)
  - `YDM.Host` (Chromium Native Messaging Console)
  - `YDM.Tests` (Automated Test Suite with Local Mock Server)
- Product Name: **YDM** (Your Download Manager)
- Tagline: *Your Downloads. Your Control.*

### 3.2 Security and Privacy Consistency
- **Browser Cookies:** Transferring the entire browser cookie jar to the desktop application violates the principle of least privilege and introduces critical vulnerability to session hijacking. YDM will only transmit cookies strictly matching the exact hostname of the requested download URL, and only when the user explicitly initiates or enables authenticated downloads.
- **Log Data Redaction:** All sensitive headers (`Authorization`, `Cookie`, `Set-Cookie`, `Proxy-Authorization`) must be scrubbed by logging sinks before output.
- **Path Traversal Protection:** URLs and `Content-Disposition` headers frequently contain malicious payloads (e.g. `../../Windows/System32/evil.dll`). YDM enforces `PathSanitizer` stripping directory paths, reserved Windows characters (`< > : " / \ | ? *`), and DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`).
- **Zone Identifier:** YDM attaches the NTFS Alternate Data Stream `Zone.Identifier` (`ZoneId=3`) to downloaded files, ensuring Windows Defender and SmartScreen inspect untrusted files from the internet.

### 3.3 Database Schema and ACID Guarantees
- The legacy documents referenced storing segment offsets in the registry or flat files.
- **Correction:** SQLite 3 with Write-Ahead Logging (WAL) mode is selected:
  - Table `Downloads`: Tracks primary metadata, status, total bytes, downloaded bytes, ETag, and destination path.
  - Table `Segments`: Tracks segment index, start offset, end offset, current offset, and status.
  - Table `Settings`: Key-value configuration for concurrency, default folders, and network timeouts.
  - Schema migrations will use a dedicated `MigrationService` with transactional versioning (`PRAGMA user_version`).

### 3.4 Dependency & License Audit
- **.NET 8.0 SDK (LTS):** Fully open-source (MIT).
- **Microsoft.Data.Sqlite:** Official Microsoft SQLite provider (MIT).
- **CommunityToolkit.Mvvm:** Microsoft open-source MVVM library (MIT).
- **xUnit & FluentAssertions:** Standard open-source testing stack (Apache 2.0 / MIT).
- **No GPL/AGPL dependencies:** YDM maintains a clean MIT license without viral licensing contamination.

---

## 4. Audit Sign-Off

The technical audit is complete. All unproven assumptions have been clarified, security boundaries established, and dependency architectures ratified. YDM proceeds directly into implementation based on verified engineering standards.
