# Contributing to YDM (Your Download Manager)

Thank you for your interest in contributing to **YDM**! We welcome bug reports, feature suggestions, architecture reviews, and pull requests.

---

## 1. Code of Conduct & Architectural Guidelines

- **Clean Architecture:** Keep domain entities and business logic (`YDM.Core`) decoupled from UI frameworks (`YDM.UI`) and network adapters (`YDM.Engine`).
- **Security First:** Enforce path sanitization, SSRF protection, and privacy policies. Never transmit full browser cookie jars or log sensitive authentication tokens.
- **Lawful Content Boundaries:** YDM does not bypass Digital Rights Management (DRM) or access control paywalls. Pull requests attempting to circumvent Widevine, PlayReady, or FairPlay will be closed immediately.
- **Clean-Room Development:** YDM is an independent implementation. Do not contribute proprietary source code, disassemblies, or copyrighted assets from other download managers.

---

## 2. Development Setup

### Prerequisites
- **Operating System:** Windows 10 or Windows 11 (x64)
- **SDK:** [.NET 8.0 SDK (LTS)](https://dotnet.microsoft.com/download/dotnet/8.0)
- **IDE:** Visual Studio 2022 (with .NET Desktop Development workload) or VS Code with C# Dev Kit.

### Building & Testing
Clone the repository:
```powershell
git clone https://github.com/ydm-downloader/ydm.git
cd ydm
```

Restore dependencies and build:
```powershell
dotnet restore YDM.sln
dotnet build YDM.sln
```

Run automated tests:
```powershell
dotnet test tests/YDM.Tests/YDM.Tests.csproj
```

Run the desktop client:
```powershell
dotnet run --project src/YDM.UI/YDM.UI.csproj
```

---

## 3. Pull Request Process

1. Fork the repository and create a feature branch from `main`:
   ```powershell
   git checkout -b feature/my-new-feature
   ```
2. Write unit or integration tests in `tests/YDM.Tests` covering your modifications.
3. Ensure all tests pass with zero warnings:
   ```powershell
   dotnet test
   ```
4. Commit with descriptive messages.
5. Open a Pull Request against `main`.
