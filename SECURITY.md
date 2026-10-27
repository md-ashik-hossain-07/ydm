# Security Policy

## 1. Supported Versions

Security updates are actively maintained for the following versions:

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |
| < 1.0   | :x:                |

---

## 2. Reporting a Vulnerability

The YDM development team takes security and user privacy seriously. If you discover a vulnerability, please report it responsibly:

- **Do NOT** disclose security vulnerabilities publicly via GitHub issues or discussions.
- Email vulnerability details to the core maintainers or submit a private GitHub Security Advisory via the repository's **Security** tab.
- Include a minimal reproducible example or step-by-step instructions.

### What to Expect:
- You will receive an initial acknowledgment within 48 hours.
- A fix will be developed, tested against regression suites, and published in a security patch release.

---

## 3. Threat Model Safeguards

YDM implements architectural safeguards against common desktop download threats:

1. **Path Traversal Mitigation (`PathSanitizer`):** Rejects directory separators (`../`, `..\`), strips illegal Windows characters, and handles reserved DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`).
2. **SSRF & Local IP Blocking (`UrlSecurityValidator`):** Connections to `127.0.0.1`, loopback, link-local metadata endpoints (`169.254.169.254`), and private subnets (`10.0.0.0/8`, `192.168.0.0/16`, `172.16.0.0/12`) are blocked by default.
3. **SmartScreen Zone Identifier:** Downloaded files on NTFS volumes are tagged with `Zone.Identifier` (`ZoneId=3`), ensuring Windows Defender inspects untrusted internet binaries.
4. **Least-Privilege Browser Integration:** No full browser cookie harvesting; domain cookies are only gathered for the specific download URL upon explicit user authorization.
5. **No Auto-Run:** YDM never executes downloaded files automatically.
