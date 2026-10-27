# YDM Release Readiness & Independent Verification Audit

**Document Version:** 1.0.0  
**Project:** YDM (Your Download Manager)  
**Audit Date:** October 28, 2026  
**Auditor:** Lead Software Architect & Implementation Engineer  
**Workspace:** `E:\APP BUILD\Your Download Manager`  
**Git Commit:** `f812317` (`master`)  

---

## 1. Executive Verdict: Conditional Pre-Release Ready

YDM has been independently verified through clean builds, automated integration tests, runtime process executions, and SQLite database inspections.

- **Core Download Engine:** **Ready.** Multi-connection HTTP Range requests, dynamic midpoint segment splitting, pause/resume, and SHA-256 byte-exact assembly are fully functional and tested.
- **Storage & State Recovery:** **Ready.** SQLite Write-Ahead Logging (WAL) mode persists jobs, segment checkpoints, and configuration across application restarts.
- **Desktop Application (WPF):** **Ready.** Self-contained executable (`publish/ui/YDM.UI.exe`) launches, creates the database tables, and runs independently without requiring pre-installed .NET runtimes.
- **Browser Native Messaging Bridge:** **Ready.** Process-level testing confirms `YDM.Host.exe` communicates via 32-bit little-endian stdio framing and Named Pipes (`\\.\pipe\YDM_IPC`).
- **Distribution Blockers:**
  1. **No Code-Signing Certificate:** Binaries are unsigned; Windows SmartScreen will display an informational prompt on end-user machines.
  2. **Inno Setup Compiler (`iscc.exe`) Missing Locally:** The installer script [`installer/ydm_setup.iss`](../installer/ydm_setup.iss) is complete, but cannot be compiled into `ydm_setup.exe` on this machine without installing Inno Setup 6. Portable self-contained packaging is functional.
  3. **No Remote Git Repository:** Code is committed locally to `master`. No remote is configured and no files have been published to GitHub.

---

## 2. Real Workspace & File Verification

### 2.1 Workspace Identity
- **Absolute Path:** `E:\APP BUILD\Your Download Manager`
- **Branding Check:** Verified that all application titles, namespaces, projects, solutions, manifests, and documentation use **YDM (Your Download Manager)**. Legacy name *NovaFetch* appears exclusively in `docs/TECHNICAL_AUDIT.md` where the audit and migration from the legacy research is documented.
- **Research Archive:** All 9 original research documents are preserved under [`docs/research/`](../docs/research/).

### 2.2 Project Structure & File Integrity
Every file has been verified to exist on disk with real, non-empty implementation content:
- **Solution:** [`YDM.sln`](../YDM.sln)
- **Projects:**
  - [`src/YDM.Core`](../src/YDM.Core) (Class Library, .NET 8.0)
  - [`src/YDM.Storage`](../src/YDM.Storage) (Class Library, .NET 8.0, Microsoft.Data.Sqlite 8.0.10)
  - [`src/YDM.Engine`](../src/YDM.Engine) (Class Library, .NET 8.0)
  - [`src/YDM.Queue`](../src/YDM.Queue) (Class Library, .NET 8.0)
  - [`src/YDM.Host`](../src/YDM.Host) (Console Application, .NET 8.0)
  - [`src/YDM.UI`](../src/YDM.UI) (WPF Application, .NET 8.0-windows, CommunityToolkit.Mvvm 8.3.2)
  - [`tests/YDM.Tests`](../tests/YDM.Tests) (xUnit Test Project, .NET 8.0)
- **Extension & Bridge:** [`extension/`](../extension/) (Manifest V3), [`host/`](../host/)
- **Installer:** [`installer/ydm_setup.iss`](../installer/ydm_setup.iss), [`installer/install.ps1`](../installer/install.ps1)
- **CI/CD:** [`.github/workflows/build-and-test.yml`](../.github/workflows/build-and-test.yml), [`.github/workflows/release.yml`](../.github/workflows/release.yml)

---

## 3. Fresh Verification Commands & Results

All commands were executed in the project root with the active .NET 8.0.427 SDK.

### 3.1 Environment Verification: `dotnet --info`
- **Command:** `dotnet --info`
- **Exit Code:** `0`
- **Output:**
  ```text
  .NET SDK:
   Version:           8.0.427
   MSBuild version:   17.11.69+4d6a29faa
  Runtime Environment:
   OS Name:     Windows
   OS Version:  10.0.26300
   RID:         win-x64
  .NET runtimes installed:
    Microsoft.AspNetCore.App 8.0.33
    Microsoft.NETCore.App 8.0.33
    Microsoft.WindowsDesktop.App 8.0.33
  ```

### 3.2 Solution Restore: `dotnet restore YDM.sln`
- **Command:** `dotnet restore YDM.sln`
- **Exit Code:** `0`
- **Output:**
  ```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  ```

### 3.3 Solution Build: `dotnet build YDM.sln --configuration Release`
- **Command:** `dotnet build YDM.sln --configuration Release`
- **Exit Code:** `0`
- **Output:**
  ```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  YDM.Core -> E:\APP BUILD\Your Download Manager\src\YDM.Core\bin\Release\net8.0\YDM.Core.dll
  YDM.Storage -> E:\APP BUILD\Your Download Manager\src\YDM.Storage\bin\Release\net8.0\YDM.Storage.dll
  YDM.Host -> E:\APP BUILD\Your Download Manager\src\YDM.Host\bin\Release\net8.0\YDM.Host.dll
  YDM.Engine -> E:\APP BUILD\Your Download Manager\src\YDM.Engine\bin\Release\net8.0\YDM.Engine.dll
  YDM.Queue -> E:\APP BUILD\Your Download Manager\src\YDM.Queue\bin\Release\net8.0\YDM.Queue.dll
  YDM.Tests -> E:\APP BUILD\Your Download Manager\tests\YDM.Tests\bin\Release\net8.0\YDM.Tests.dll
  YDM.UI -> E:\APP BUILD\Your Download Manager\src\YDM.UI\bin\Release\net8.0-windows\YDM.UI.dll

  Build succeeded.
      0 Warning(s)
      0 Error(s)
  Time Elapsed 00:00:08.31
  ```

### 3.4 Automated Test Suite: `dotnet test tests/YDM.Tests/YDM.Tests.csproj --configuration Release`
- **Command:** `dotnet test tests\YDM.Tests\YDM.Tests.csproj --configuration Release`
- **Exit Code:** `0`
- **Output:**
  ```text
  Starting test execution, please wait...
  A total of 1 test files matched the specified pattern.

  Passed!  - Failed: 0, Passed: 38, Skipped: 0, Total: 38, Duration: 956 ms - YDM.Tests.dll (net8.0)
  ```

---

## 4. Actual Application Behavior Verification

### 4.1 Desktop Client Execution (WPF)
- **Framework-Dependent Launch Issue:** The .NET 8 SDK was installed in user folder `C:\Users\HP\.dotnet` rather than `C:\Program Files\dotnet`. The framework-dependent apphost (`YDM.UI.exe`) failed to resolve `hostfxr.dll` (Error `0x80008083`) unless `DOTNET_ROOT` was exported into the environment.
- **Self-Contained Executable Solution:** Executed `dotnet publish src/YDM.UI/YDM.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/ui`. The resulting single-file executable (`publish/ui/YDM.UI.exe`, 154 MB) bundles its own runtime and was verified to launch and run with `$env:DOTNET_ROOT = $null`.
- **WPF Async Startup Fix:** In initial runs, `App.xaml.cs` used `async void OnStartup` while `ShutdownMode` defaulted to `OnLastWindowClose`. Because no window was open when the first `await` yielded, WPF exited prematurely. **Fix applied:** Set `ShutdownMode = ShutdownMode.OnExplicitShutdown` during initialization, then `ShutdownMode = ShutdownMode.OnMainWindowClose` after `mainWindow.Show()`.
- **Database Table Verification:** Verified via SQLite that `%LOCALAPPDATA%\YDM\ydm.db` was created and contains tables `Downloads`, `Segments`, and `Settings`.

### 4.2 Core Engine & Multi-Connection Testing
- Verified through integration tests with in-process `MockHttpServer`:
  - 4-connection segmented download of 512 KB: Completed, assembled, and verified with `SHA256.HashData`.
  - Non-range degradation: Server with `Accept-Ranges: none` gracefully degraded to 1 stream, completed, and passed SHA-256 verification.
  - Pause and resume: Download paused mid-flight, resumed, and passed SHA-256 verification.
  - Invalidation on ETag change: Server updated ETag while paused; engine detected invalidation, discarded stale segments, and restarted from byte 0 cleanly.
  - Mid-stream cancellation: Canceled download cleaned up without leaving a corrupted destination file.

### 4.3 Browser Native Messaging Communication
- Tested live execution of `YDM.Host.exe` by feeding a 4-byte unsigned little-endian length prefix followed by UTF-8 JSON `{"action":"ping"}` to stdin.
- `YDM.Host.exe` returned a 4-byte length prefix and valid JSON payload `{"Status":"ok","Message":"YDM Native Host is active"}` to stdout.

### 4.4 Real Installer Status
- **Script:** [`installer/ydm_setup.iss`](../installer/ydm_setup.iss) is complete and configures start menu shortcuts, uninstallation keys, and Chromium Native Messaging registry entries.
- **Local Tool Blocker:** Inno Setup compiler (`iscc.exe`) is **not installed** on this machine (`Get-Command iscc.exe` returned not found). Thus, `ydm_setup.exe` cannot be compiled locally until Inno Setup is installed.
- **Cloud Packaging:** Configured in [`.github/workflows/release.yml`](../.github/workflows/release.yml) to produce portable ZIP and checksums on GitHub tag push.

---

## 5. Investigation of Previous Failures & Corrections

| Failure Scenario | Root Cause | Corrective Action & Final Verification |
|:---|:---|:---|
| **CS0206 Build Error in `DownloadEngine.cs`** | `Interlocked.Add(ref job.DownloadedBytes, 0)` failed because `DownloadedBytes` is a property, not a field. | Replaced with `var progressReporter = new Progress<long>(_ => { });`. Build succeeded. |
| **`dotnet new wpf` Flag Error** | Passed `-f net8.0-windows` to `dotnet new wpf`, but the template expects `-f net8.0`. | Created with `-f net8.0`. Succeeded. |
| **PathSanitizer `aux.tar.gz` Test Failure** | `Path.GetFileNameWithoutExtension("aux.tar.gz")` returned `"aux.tar"`, failing to catch `"aux"`. | Updated sanitizer to inspect the root name before all extensions (`fileName.Split('.')[0]`). Passed. |
| **Probe Service Redirect Test Failure** | `HttpClient` followed 302 redirects automatically and dropped intermediate `Content-Disposition`. | Implemented explicit redirect loop in `HttpProbeService` with `AllowAutoRedirect = false`. Passed. |
| **Edge Case Tests Timing Out / Race Condition** | In localhost tests without throttling, transfers finished in < 5 ms, completing before the test pause delay triggered. | Added `SlowMode` (simulated latency) in `MockHttpServer` for deterministic pause/cancel testing. Passed. |
| **Git Commit Identity Missing** | Fresh git repository had no default `user.name` or `user.email`. | Set local repository config `user.name "YDM Contributor"` and committed cleanly. |
| **HostFXR Resolution (`0x80008083`)** | Framework-dependent apphost could not find .NET in standard `Program Files\dotnet`. | Published self-contained single-file executable (`publish/ui/YDM.UI.exe`). Verified standalone execution. |
| **WPF Early Shutdown on Startup** | `async void OnStartup` returned before `mainWindow.Show()`, causing WPF to shut down. | Explicitly managed `ShutdownMode`. Database and UI now initialize cleanly. |

---

## 6. Git Status & Remote Audit

- **Branch:** `master`
- **Working Tree:** Clean (zero uncommitted changes)
- **Commit History:**
  - `f812317` — *fix(ui): prevent premature WPF shutdown on startup and add live native host process test*
  - `3b7f204` — *feat: complete Phase 2 implementation of YDM*
- **Remote Configuration:** `git remote -v` returns empty. No remote repository has been added and no code has been pushed.

---

## 7. Remaining Blockers Before Public Release

1. **Remote Repository Setup:** Configure GitHub remote (`git remote add origin https://github.com/...`) and obtain user authorization before pushing.
2. **Code Signing:** Binaries are currently unsigned. To remove the Windows SmartScreen warning, an EV code-signing certificate or Azure Trusted Signing profile must be configured.
3. **Inno Setup Installation:** Install Inno Setup 6 locally if compiling `ydm_setup.exe` on this developer machine is required, or allow GitHub Actions to build release artifacts.
4. **Browser Store Publication:** Submitting the extension to the Chrome Web Store and Edge Add-ons catalog requires developer registrations. Until published, users load unpacked from [`extension/`](../extension/).
