# YDM — Your Download Manager

> **Your Downloads. Your Control.**

[![Build & Test](https://github.com/placeholder-owner/ydm/actions/workflows/build-and-test.yml/badge.svg)](#build-and-test)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows 10/11](https://img.shields.io/badge/Platform-Windows%2010%2F11%20x64-brightgreen.svg)](#system-requirements)
[![Target Framework: .NET 8.0](https://img.shields.io/badge/.NET-8.0%20LTS-purple.svg)](#building-from-source)

**YDM (Your Download Manager)** is an independent, high-performance, open-source download manager engineered for Windows 10 and Windows 11 (64-bit). Built from clean-room architectural principles, YDM delivers reliable multi-connection downloads, resilient pause and resume capabilities, SQLite-backed state persistence, queue management, and privacy-first Chromium browser integration.

---

## Table of Contents
1. [Core Features](#1-core-features)
2. [System Requirements](#2-system-requirements)
3. [Installation Methods](#3-installation-methods)
4. [Uninstallation Instructions](#4-uninstallation-instructions)
5. [Browser Extension Setup](#5-browser-extension-setup)
6. [Building from Source](#6-building-from-source)
7. [Running the Automated Test Suite](#7-running-the-automated-test-suite)
8. [Troubleshooting Guide](#8-troubleshooting-guide)
9. [Architecture & Security Principles](#9-architecture--security-principles)
10. [Legal Disclaimer & Licensing](#10-legal-disclaimer--licensing)

---

## 1. Core Features

- **Multi-Connection HTTP Range Acceleration:** Probes remote server capabilities (RFC 7233 / RFC 9110 `206 Partial Content`, `Accept-Ranges: bytes`) and splits files across parallel TCP connections.
- **Dynamic In-Flight Segmentation:** Divides the largest active incomplete segment at its midpoint during download, keeping connections saturated without gaps or overlapping ranges.
- **Resilient Pause & Resume:** Downloads resume from verified on-disk checkpoints. Validates remote server state via `ETag` and `Last-Modified` timestamps.
- **Graceful Single-Stream Fallback:** If a remote server ignores Range requests (`200 OK`) or omits byte ranges, YDM automatically degrades to single-stream sequential downloading.
- **ACID Persistence (SQLite WAL):** Download histories, segment offsets, and queue states persist in an ACID SQLite 3 database with Write-Ahead Logging (`ydm.db`), surviving system restarts and power outages.
- **Queue & Priority Management:** Concurrency throttler (`SemaphoreSlim`) limits active downloads (default: 2 simultaneous), manages priorities (`High`, `Normal`, `Low`), and auto-dequeues pending jobs.
- **Security & Privacy by Design:**
  - Strict path sanitization (`PathSanitizer`) strips directory traversal attempts (`../`, `..\`) and reserved DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`).
  - SSRF protection blocks loopback (`127.0.0.1`, `::1`) and RFC 1918 private IP addresses by default.
  - Attaches NTFS `Zone.Identifier` (`ZoneId=3`) so Windows Defender and SmartScreen inspect downloaded binaries.
  - Zero telemetry, zero analytics tracking, and no complete browser cookie jar harvesting.
- **Chromium Manifest V3 Integration:** Lightweight extension for Chrome and Edge communicating over sandboxed Native Messaging (`stdio`). Only cancels browser downloads after YDM acknowledges job acceptance.

---

## 2. System Requirements

- **Operating System:** Windows 10 (version 1809 or higher) or Windows 11 (64-bit).
- **Runtime:** Fully self-contained. No external .NET runtime installation required when using the official installer or portable release.
- **Disk Space:** Approximately 160 MB for installation.

---

## 3. Installation Methods

### Option A: Windows Setup Installer (Recommended)
1. Download `ydm_setup_1.0.0_win-x64.exe` from the latest release on your GitHub repository.
2. Run the installer wizard:
   - Configures application files in `%LOCALAPPDATA%\Programs\YDM` (or `Program Files`).
   - Automatically registers the Chromium Native Messaging bridge for Chrome and Edge in the Windows Registry (`HKCU`).
   - Optionally creates Desktop and Startup shortcuts.
3. *Windows SmartScreen Note:* Because official releases are distributed unsigned, Windows SmartScreen will display an informational banner (*"Windows protected your PC"*). Click **More info** -> **Run anyway**.

### Option B: PowerShell Fast Install
To download, verify SHA-256 integrity, and launch the installer via PowerShell:
```powershell
# Replace '<owner>/<repo>' with the official repository (e.g. 'your-username/ydm')
.\installer\install.ps1 -Repository '<owner>/<repo>'
```
> **Security Guarantee:** The script downloads the official release `checksums.txt` manifest over HTTPS, verifies the SHA-256 hash of the downloaded installer, and will abort immediately if the checksum does not match.

### Option C: Portable Application (Zero-Install)
1. Download `ydm-v1.0.0-win-x64-portable.zip` from releases.
2. Extract the archive to any folder.
3. Launch `YDM.UI.exe`.

---

## 4. Uninstallation Instructions

### Standard Windows Uninstallation
1. Open Windows **Settings** (`Win + I`) -> **Apps** -> **Installed apps**.
2. Locate **Your Download Manager**, click the three dots (`...`), and select **Uninstall**.
3. The uninstaller removes all application binaries, desktop shortcuts, and automatically removes the Chromium Native Messaging host keys from `HKCU\Software\Google\Chrome\NativeMessagingHosts` and `HKCU\Software\Microsoft\Edge\NativeMessagingHosts`.

### Optional User Data Cleanup
Download history, configuration, and partial temporary files are stored in your local profile. To remove them completely after uninstalling:
```powershell
Remove-Item -Path "$env:LOCALAPPDATA\YDM" -Recurse -Force -ErrorAction SilentlyContinue
```

---

## 5. Browser Extension Setup

To enable browser download interception and context menu integration ("Download with YDM"):

1. **Register the Native Host Bridge:**
   Run the registration script with PowerShell:
   ```powershell
   pwsh host/register_host.ps1
   ```
2. **Load the Manifest V3 Extension:**
   - In Google Chrome, go to `chrome://extensions/` (or `edge://extensions/` in Microsoft Edge).
   - Enable **Developer mode** (toggle in top right).
   - Click **Load unpacked** and select the [`extension/`](extension/) directory in this repository.
3. **Verify Active Connection:**
   - Click the YDM icon in the browser toolbar.
   - The status badge will display **YDM Desktop App: Active**.

---

## 6. Building from Source

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher.
- Git for Windows.
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) (optional, only needed for compiling `ydm_setup.exe`).

### Build Commands
```powershell
# 1. Clone repository
git clone <repository-url>
cd ydm

# 2. Restore dependencies
dotnet restore YDM.sln

# 3. Build Release configuration
dotnet build YDM.sln --configuration Release

# 4. Publish Self-Contained Desktop App
dotnet publish src/YDM.UI/YDM.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/ui
dotnet publish src/YDM.Host/YDM.Host.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/host

# 5. Compile Windows Installer (with Inno Setup 6)
iscc installer/ydm_setup.iss
```

The resulting setup installer is saved in `dist/ydm_setup_1.0.0_win-x64.exe`.

---

## 7. Running the Automated Test Suite

YDM includes 38 automated unit and integration tests executing against an in-process local HTTP mock server:

```powershell
dotnet test tests/YDM.Tests/YDM.Tests.csproj --configuration Release
```

### Verified Scenarios:
- Multi-connection partial content downloads with bit-exact SHA-256 verification.
- Graceful degradation on non-range servers (`Accept-Ranges: none`).
- Safe pause and resume without byte loss.
- Server ETag alteration detection and safe segment invalidation on resume.
- Cancellation mid-flight without leaving corrupted final destination files.
- Path traversal rejection and Windows DOS device name sanitization (`CON`, `_aux.tar.gz`).
- SSRF loopback and private IP blocking.
- Process restart recovery in SQLite database.
- Process-level stdio Native Messaging framing (`YDM.Host.exe`).

---

## 8. Troubleshooting Guide

### 8.1 Windows SmartScreen Warning
- **Symptom:** Windows displays *"Windows protected your PC - Unknown Publisher"*.
- **Cause:** Open-source release binaries are distributed without an expensive commercial EV code-signing certificate.
- **Resolution:** Click **More info**, verify that the application name says `ydm_setup_1.0.0_win-x64.exe`, and click **Run anyway**.

### 8.2 Browser Extension Shows "Disconnected"
- **Symptom:** Extension popup says *"YDM Desktop App: Disconnected"*.
- **Resolution:**
  1. Ensure the YDM desktop application (`YDM.UI.exe`) is running.
  2. Verify that `host/register_host.ps1` was executed to register the host keys in the Windows Registry.
  3. Ensure no antivirus or firewall is blocking local Named Pipe communication (`\\.\pipe\YDM_IPC`).

### 8.3 Network Downloads Fail on Corporate Proxies
- **Symptom:** Connection timeouts or SSL handshake errors.
- **Resolution:** YDM uses Windows system proxy settings by default via .NET's `SocketsHttpHandler`. Ensure corporate CA root certificates are installed in the Windows Trusted Root Store.

---

## 9. Architecture & Security Principles

- **No Complete Cookie Harvesting:** YDM never captures or exports a full browser cookie jar. If authenticated downloads require session cookies, only cookies strictly matching the download URL hostname are retrieved upon explicit user consent.
- **Lawful Content Boundaries:** YDM **strictly refuses** Digital Rights Management (DRM) circumvention. Widevine, FairPlay, and PlayReady encrypted streams cannot be captured.
- **Clean-Room Implementation:** YDM contains 100% original C# code and original styling. It does not contain decompiled code or proprietary assets.

---

## 10. Legal Disclaimer & Licensing

YDM is an independent open-source project. It is **not affiliated with, endorsed by, or sponsored by** Tonec Inc. or Internet Download Manager (IDM). All trademarks and product names belong to their respective owners.

YDM is licensed under the [MIT License](LICENSE).
