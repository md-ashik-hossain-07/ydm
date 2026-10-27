# YDM (Your Download Manager) v1.0.0

> **Your Downloads. Your Control.**

We are pleased to announce the initial open-source release of **YDM (Your Download Manager)**, a modern, independent, high-performance download manager engineered specifically for Windows 10 and Windows 11 (64-bit).

Built entirely from clean-room architectural principles in C# and .NET 8, YDM delivers resilient multi-connection acceleration, atomic state recovery, intelligent queue management, and privacy-first Chromium browser integration without proprietary dependencies or bloatware.

---

## 🚀 Key Features & Highlights

- **Multi-Connection HTTP Range Acceleration:** Probes remote servers for RFC 7233 / RFC 9110 compliance (`206 Partial Content`, `Accept-Ranges: bytes`) and establishes parallel TCP connections to accelerate throughput.
- **Dynamic In-Flight Segmentation:** Intelligently splits the largest active incomplete segment at its midpoint during download, keeping connections saturated without gaps or overlapping ranges.
- **Resilient Pause & Resume:** State is snapshotted to disk continuously. Validates remote server state via HTTP `ETag` and `Last-Modified` timestamps to protect against corrupted partial resumes.
- **Graceful Single-Stream Fallback:** If a remote server does not support byte ranges (`200 OK`) or omits Content-Length, YDM automatically degrades to a single-stream sequential transfer.
- **ACID Persistence (SQLite WAL):** Download jobs, segment byte offsets, and configuration persist in an ACID SQLite 3 database with Write-Ahead Logging (`%LOCALAPPDATA%\YDM\ydm.db`), surviving system restarts and sudden power interruptions.
- **Queue & Priority Management:** Concurrency throttler (`SemaphoreSlim`) limits active downloads (default: 2 simultaneous), manages priorities (`High`, `Normal`, `Low`), and auto-dequeues pending jobs.
- **Privacy & Security by Design:**
  - **Path Sanitization:** `PathSanitizer` neutralizes directory traversal attempts (`../`, `..\`) and reserved DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`).
  - **SSRF Protection:** `UrlSecurityValidator` rejects loopback (`127.0.0.1`, `::1`) and RFC 1918 private IP addresses by default.
  - **Zone Identifier:** Attaches NTFS `Zone.Identifier` (`ZoneId=3` / Internet) so Windows Defender and SmartScreen inspect downloaded binaries upon completion.
  - **Zero Telemetry:** YDM includes zero analytics tracking, zero third-party ads, and strictly refuses complete browser cookie harvesting.
- **Chromium Manifest V3 Integration:** Lightweight extension for Google Chrome and Microsoft Edge communicating over sandboxed Native Messaging (`stdio`). Only cancels browser downloads after YDM acknowledges job acceptance.

---

## 📦 Download Assets & Checksums

| File Name | Size | SHA-256 Checksum | Description |
| :--- | :---: | :--- | :--- |
| **`ydm_setup_1.0.0_win-x64.exe`** | ~70.3 MB | `0e13cc6ed2cab4ab254dfb47d9150fdd234a202b47bbfee6f4c4f295da8c028c` | Official Windows Setup Installer (per-user, no UAC prompt required). |
| **`ydm-v1.0.0-win-x64-portable.zip`** | ~96.3 MB | `3af0522fba6252f452ae4611c533bb6039f0a1cb846e8d1df098ceae1f386f2f` | Zero-install standalone portable zip archive. |
| **`checksums.txt`** | 196 B | — | Cryptographic SHA-256 integrity manifest. |

---

## 💻 System Requirements

- **Operating System:** Windows 10 (version 1809 or higher) or Windows 11 (64-bit).
- **Runtime:** Completely self-contained. **No external .NET runtime installation required.**
- **Architecture:** x64 (AMD64 / Intel 64).

---

## 🛡️ Windows SmartScreen Notice

Because YDM is an independent open-source project and release binaries are distributed without an expensive commercial EV code-signing certificate, Windows SmartScreen will display an informational banner (*"Windows protected your PC"*):

1. Click **More info**.
2. Verify the application name says `ydm_setup_1.0.0_win-x64.exe` (or `YDM.UI.exe`).
3. Click **Run anyway**.

---

## 🛠️ Verification & Test Summary

Before preparing this release, the application was subjected to rigorous local verification:
- **Build:** Compiled in `Release` configuration with 0 warnings and 0 errors.
- **Automated Tests:** 38 unit and integration tests passed (100% pass rate) covering multi-connection transfers, pause/resume, single-stream fallback, SSRF prevention, path sanitization, and native messaging framing.
- **Clean Installation:** Verified via Inno Setup in silent mode (`ExitCode: 0`), properly registering shortcuts, registry keys, and uninstaller.
- **Runtime Launch:** Verified standalone launch and database creation in `%LOCALAPPDATA%\YDM\ydm.db`.
- **Clean Uninstallation:** Verified via `unins000.exe` (`ExitCode: 0`), leaving no orphaned registry keys or application files.

---

## 📜 Legal Disclaimer & License

YDM is an independent, clean-room open-source project licensed under the [MIT License](https://github.com/md-ashik-hossain-07/ydm/blob/main/LICENSE).

YDM is **not affiliated with, endorsed by, or sponsored by** Tonec Inc. or Internet Download Manager (IDM). All trademarks belong to their respective owners.
