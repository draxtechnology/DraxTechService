# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build main service
dotnet build Drax360NetworkManager/Drax360Service.csproj

# Run in console mode (for development)
dotnet run --project Drax360NetworkManager/Drax360Service.csproj

# Build utility projects
dotnet build SerialTest/SerialTest.csproj
dotnet build "Taktis Receive/Taktis Receive.csproj"
```

All projects target **.NET 10** (the service is `net10.0-windows`; the utilities are `net10.0`). The service was retargeted from .NET Framework 4.8.1 to net8 on 2026-05-10 (to align with the DraxTechClient codebase), then forward to net10 on 2026-05-12 once it became clear net8 had already entered Maintenance phase — net10 LTS runs through November 2028.

There is no automated test suite. `SerialTest` is a manual utility for verifying serial port connectivity.

## Architecture

Drax360Service is a **Windows Service** that bridges serial/network communication between fire alarm panels (from multiple manufacturers) and an AMX automation system.

### Data flow

```
Fire Alarm Panel (Serial/TCP)
    → AbstractPanel subclass (parse raw protocol bytes)
    → CustomEventArgs (normalized event)
    → DraxService (routing/orchestration)
    → AMXTransfer (TCP to localhost:3090)
    → AMX automation system
```

### Key components

**`DraxService.cs`** — The service entry point (`ServiceBase`, from `System.ServiceProcess.ServiceController`). Reads `App.config` to determine which panel type is active, instantiates the correct panel, manages the AMX TCP connection, and runs a named pipe server for inter-process communication with external tools.

**`Panels/AbstractPanel.cs`** — Base class for all panel drivers. Defines the contract (`StartUp()`, `Parse()`, `Evacuate()`, `Alert()`, `Silence()`, `Reset()`, etc.) and manages serial port I/O and event firing via `OutsideEvents`. Each manufacturer has a concrete subclass: `PanelGent`, `PanelMorleyZX`, `PanelMorelyMax`, `PanelNotifier`, `PanelPearl`, `PanelRSM`, `PanelSyncro`, `PanelTaktis`, `PanelEmail`, `PanelAdvanced`, `PanelEspa`.

**`CSAMX/AMXTransfer.cs`** — Singleton TCP client to the AMX system. Handles bidirectional messaging with heartbeat/keepalive plus a writer queue + MAK ack gate (replaces the old Thread.Sleep pacing).

**`CSAMX/CSAMX.cs`** — Singleton that manages NWM (Network Work Module) data structures and a file-based message queue for event data.

**`SettingsSingleton.cs`** — INI file reader (singleton). Each panel type has its own INI file in `Drax360NetworkManager/ini/` (e.g., `GenMan.ini` for Gent, `MAXMan.ini` for Morley MAX, `Takman.ini` for Taktis).

**`Data/`** — EF Core 10 contexts. `EspaEventsContext` is the template; `EspaEventsLegacyMigrator` handles the one-shot raw-SQL migration from the legacy v0 ESPA schema.

**`Security/AesDecryption.cs`** — AES-256/PBKDF2 decryption supporting OpenSSL format with salt, used for encrypted panel configurations.

### Configuration

`App.config` (copied to `DraxTechnology.dll.config` at build time on .NET Core) controls runtime behaviour:
- `Panels` — active panel type (e.g., `GENT`, `MORLEYMAX`, `RSM`, `TAKTIS`)
- `Configuration` — base folder path for INI files and logs
- `FakeMode` — set to `1` to run without real hardware

Per-panel serial settings (baud rate, parity, data bits, stop bits, COM port) live in the corresponding INI file under `ini/`.

### Service installation

- Service name: `DraxTechnology`, runs as `LocalService`, **manual start** (Mike/James want a fresh install not to auto-start). The start-mode is set via WiX `Start="demand"` in `DraxServiceSetup\Product.wxs`.
- `DraxServiceSetup` (WiX 5 SDK-style `.wixproj`) produces the bare MSI (`DraxTechnologyServiceSetup.msi`).
- `DraxServiceBootstrapper` (WiX 5 bundle) wraps the MSI behind a silent .NET 10 Desktop Runtime install for fresh-machine deploys. Same EXE as `DraxTechClient/DraxClientSetup` (the Desktop Runtime is a strict superset of the base runtime), so the same `windowsdesktop-runtime-10.0.x-win-x64.exe` file just needs to be dropped into both bootstrapper folders before building — see `Bundle.wxs` comments. The service doesn't actually use WinForms/WPF; we trade ~30 MB on the install for one-runtime-EXE-per-patch maintenance.
- The legacy `ProjectInstaller.cs` (System.Configuration.Install) is gone — WiX owns service registration entirely via `<ServiceInstall>` + `<ServiceControl>`.

### Ports & protocols

| Target | Protocol | Default |
|--------|----------|---------|
| AMX system | TCP | localhost:3090 |
| RSM panel | TCP | port 1471 |
| Fire alarm panels | RS-232/RS-485 serial | per INI config |
| IPC (tools/UI) | Named pipe | — |
