# YDM Known Limitations & Non-Goals

**Document Version:** 2.0.0  
**Project:** YDM (Your Download Manager)  
**Date:** October 2026  

---

## 1. Known Limitations

### 1.1 Platform Availability
- **Initial Target:** Windows 10 and Windows 11 (x64).
- **Non-Target Platforms:** Linux, macOS, Android, and iOS are not supported in the initial release. Core business logic (`YDM.Core`, `YDM.Engine`, `YDM.Storage`) is cross-platform .NET 8, but the user interface (`YDM.UI`) utilizes Windows Presentation Foundation (WPF) and Windows-specific shell integrations (Zone.Identifier ADS, Windows Tray). Cross-platform UI (via Avalonia or MAUI) is a potential future consideration.

### 1.2 Binary Code Signing
- **Unsigned Initial Releases:** Binaries built and published via GitHub Releases will initially be unsigned unless a validated code-signing certificate (such as Azure Trusted Signing or a hardware token EV certificate) is provided by the repository maintainer.
- **Impact on Users:** Windows SmartScreen will display an informational prompt (*"Windows protected your PC - Unknown Publisher"*) on initial execution. Instructions for safely unblocking and running the installer are provided in user documentation.

### 1.3 Windows Package Manager (`winget`)
- **Status:** Planned post-release.
- **Limitation:** A `winget install ydm` manifest cannot be published until the initial GitHub Release is tagged and published with valid release SHA-256 hashes. Manifest submission to `microsoft/winget-pkgs` requires manual verification by Microsoft reviewers.

### 1.4 Browser Extension Distribution
- **Status:** Unpacked developer mode distribution initially.
- **Limitation:** Publishing to the Chrome Web Store and Microsoft Edge Add-ons catalog requires developer account fees, privacy policy reviews, and automated extension auditing. The initial release provides a documented sideloading/developer-mode installation procedure.

### 1.5 Dynamic Segmentation Acceleration Realities
- **Limitation:** Dynamic multi-connection segmentation does not guarantee a fixed speed increase (e.g. "5x faster").
- **Factors Affecting Performance:**
  - Servers that throttle per-IP address or enforce aggressive rate limits will not yield speedups from parallel connections.
  - On high-bandwidth, low-latency connections, single-stream HTTP/2 or HTTP/1.1 often saturates the network link without connection splitting overhead.
  - Parallel connections are most beneficial on high-latency links where a single TCP flow cannot reach link capacity due to TCP congestion window dynamics.

---

## 2. Explicit Non-Goals & Legal Boundaries

1. **No DRM Circumvention:** YDM will never incorporate functionality to decrypt, extract, or bypass DRM access controls (including Google Widevine, Apple FairPlay, or Microsoft PlayReady). Streams requiring Encrypted Media Extensions (EME) or DRM license handshakes are rejected.
2. **No Proprietary Code Reuse:** YDM is an independent, 100% clean-room open-source project. No decompiled, copied, or proprietary code or visual assets from Internet Download Manager (Tonec Inc.) are utilized.
3. **No Unsolicited Data Collection:** YDM contains zero telemetry, zero analytics tracking, and zero advertising trackers. It connects exclusively to user-specified download URLs.
4. **No Full Cookie Harvesting:** YDM will never silently extract or mirror a user's full browser profile or global cookie jar.
