# Horizon Tweaks

Horizon is a native Windows 10/11 WPF desktop application for hardware-aware, reversible PC optimisation.

Design source: [Horizon in Figma](https://www.figma.com/design/byDfuA905mGTYI0bbDql4m/Horizon?node-id=0-1&p=f)

Technology: C#, .NET 8, WPF/XAML, MVVM, dependency injection, SQLite, JSON local state, narrowly scoped Windows PowerShell operations, plus a Node.js/TypeScript/PostgreSQL account backend.

## What is functional

- Windows, CPU, GPU/vendor/driver/VRAM, memory modules and speed, per-drive media type/health, volumes/free space, active network adapters, motherboard, BIOS, input devices, installed games, Discord, Epic, and OBS detection.
- A reusable tweak operation layer for Windows Registry values, services, power plans, TCP autotuning, and Fortnite INI values.
- Transactional execution: compatibility check, current-state read, exact backup, apply, reread, verify, and persistent active-change journal.
- Duplicate prevention (`AlreadyApplied`), administrator and restart states, external-drift detection (`UpdateChanged`), failure details, and package locks.
- Exact individual, selected, session, last-session, and all-changes restore through the native History/Restore infrastructure.
- Starter, Performance ($19.99), Ultimate ($39.99), and the specified add-on catalogue and prices. Starter is always available; paid access requires signed entitlement claims.
- Fortnite competitive settings with a per-session file backup, plus process-watched temporary Gaming Mode that restores changes after Fortnite closes or on the next app run.
- Actual Startup Run-key/folder inventory with disable/restore, AppX debloat inventory with protected Keep items, and measured cleanup scanning/deletion with a post-clean rescan.
- A repeatable measured local benchmark. No random FPS, thermal, latency, or performance values are displayed.
- Backend-driven email/password registration and sign-in, refresh/logout, email verification, password reset, Google/Discord/Epic OAuth adapters, provider linking, and account-backed onboarding state.
- Windows DPAPI-protected Horizon session storage and a system-browser OAuth flow using a short-lived one-time code returned to a temporary loopback callback.

Firmware/voltage/clock/router changes remain hardware-specific guided workflows unless a supported vendor interface is available; Horizon does not issue unsafe generic writes.

## Build and test

Requirements: Windows 10/11 and the .NET 8 SDK.

```powershell
dotnet restore Horizon.sln
dotnet build Horizon.sln -c Release
dotnet test tests/Horizon.Tests/Horizon.Tests.csproj -c Release
dotnet run --project src/Horizon.App/Horizon.App.csproj
```

The main UI runs normally. Operations request UAC only when a selected setting needs administrator rights.

Build the self-contained Windows x64 release with:

```powershell
./scripts/build-release.ps1
```

The executable is written to `artifacts/win-x64/Horizon.exe`. The optional Inno Setup definition at `installer/Horizon.iss` creates a standard per-user installer.

## Authentication backend

Start with the beginner-friendly [Horizon setup guide](SETUP.md). It explains the existing private `backend/.env`, PostgreSQL, every provider dashboard, SMTP, and each environment variable without exposing credentials.

After configuring `backend/.env`, run:

```powershell
Set-Location backend
pnpm install
pnpm migrate
pnpm dev
```

The WPF development default is `http://127.0.0.1:8787`. Override it with `HORIZON_API_BASE_URL`; non-loopback endpoints must use HTTPS. See [backend setup](backend/README.md) for Docker, Oracle Cloud, provider callback, and test instructions.

## Solution structure

```text
Horizon.sln
├── assets/                  Permanent Figma assets and embedded fonts
├── docs/                    Architecture and operation-safety notes
├── installer/               Inno Setup definition
├── scripts/                 Release build automation
├── src/
│   ├── Horizon.App/         WPF shell, views, ViewModels, theme, tray, dialogs
│   ├── Horizon.Core/        Models and service contracts
│   ├── Horizon.Infrastructure/ SQLite, settings, active-change journal
│   ├── Horizon.Tweaks/      Catalogue, compatibility, operations, engine
│   ├── Horizon.System/      Windows inventory and command runner
│   ├── Horizon.Restore/     Restore orchestration
│   └── Horizon.Services/    Cleanup, Startup, AppX, benchmark, Gaming Mode
└── tests/Horizon.Tests/     xUnit engine and operation tests
```

## Entitlement and checkout integration

Set `HORIZON_ENTITLEMENT_PUBLIC_KEY` to the RSA public key used by the account backend. Store the signed envelope at `%LOCALAPPDATA%\Horizon\entitlements.json`:

```json
{
  "payload": "base64-encoded JSON claims",
  "signature": "base64-encoded RSA SHA-256 signature"
}
```

Claims contain `accountId`, `owned`, and optional `validUntil`. Set `HORIZON_PURCHASE_URL` to the one-time checkout endpoint; Horizon appends the selected `product` identifier. Without those backend settings, paid features remain locked and checkout reports that it is not configured.

Horizon stores settings, history, backups, and session state below `%LOCALAPPDATA%\Horizon`. See [architecture](docs/architecture.md) and [operation safety](docs/safety.md) for implementation details.

Authentication is backend-driven. Provider controls report a controlled unavailable state when server credentials are absent; they never fake success. Development console email mode returns reset/verification codes for local testing, while production requires SMTP delivery. No provider client secret or provider token is compiled into Horizon Windows.

Unexpected failures are logged to `%LOCALAPPDATA%\Horizon\Logs` with the application version, current page, operation, stack trace, and inner exceptions. Expected authentication and validation failures are contained by the sign-in view and leave Horizon responsive.

Inter and Anton are embedded under the SIL Open Font License; Material Symbols is embedded under Apache License 2.0. Attribution is retained in `assets/fonts`.
"# Horizon-Tweakss" 
